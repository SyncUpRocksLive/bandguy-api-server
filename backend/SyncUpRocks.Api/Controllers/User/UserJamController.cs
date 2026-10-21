using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using SyncUpRocks.Api.Caches;
using SyncUpRocks.Api.Security;
using SyncUpRocks.Data.Access.Musician.Interfaces;
using SyncUpRocks.Data.Access.S3;
using SyncUpRocks.Data.Importers.SetList.v1;
using SyncUpRocks.Types;
using System.Threading.Channels;

namespace SyncUpRocks.Api.Controllers.User;

/// <summary>
/// TODO: Replace this - with better, safer methds. As well as backed by postgresql datalayer
/// </summary>
[Authorize]
[ApiController]
[Route("api/musician/jam")]
public class UserJamController(
    ILogger<UserJamController> _logger,
    UserMappingCache _userMappingCache,
    IMusicianDataAccess _musicianDataAccess,
    SongInformationCache _songInformationCache,
    IS3DataTransfer _dataTransfer,
    IS3ClientProvider _s3ClientProvider,
    SetlistImporter _setListImporter) : ControllerBase
{
    public record MessageBody(
        string Type,
        JsonNode Value
    );

    /// <summary>
    /// For simplicity right now - keeping same contract between send/receive. On send though, from userid/name is overriden
    /// </summary>
    /// <param name="ToUserId"></param>
    /// <param name="FromUserId"></param>
    /// <param name="FromUsername"></param>
    /// <param name="SentUtc"></param>
    /// <param name="MessageData"></param>
    public record MessageItemRequest(
        string FromUserConnectionIdentifier,
        Guid ToUserId,
        string ToUserConnectionIdentifier,
        long SentUtc,
        MessageBody MessageData
    );

    public record MessageItem(
        Guid ToUserId,
        string ToUserConnectionIdentifier,
        Guid FromUserId,
        string FromUserConnectionIdentifier,
        string FromUsername,
        long SentUtc,
        MessageBody MessageData
    );

    // Each channel has a queue of messages for each user. This is a simple in-memory queue for now, but will be replaced with a more robust solution in the future.
    private static ConcurrentDictionary<string, ConcurrentDictionary<string, Channel<MessageItem>>> _channelMessages = new();

    public ChannelReader<MessageItem> RegisterUser(string channelIdentifier, string userId, string connectionIdentifier)
    {
        var jamConnections = _channelMessages.GetOrAdd(channelIdentifier, static _ => new ConcurrentDictionary<string, Channel<MessageItem>>());

        var channel = Channel.CreateBounded<MessageItem>(new BoundedChannelOptions(100)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        jamConnections[$"{userId}:{connectionIdentifier}"] = channel;
        return channel.Reader;
    }

    public void UnregisterUser(string channelIdentifier, string userId, string connectionIdentifier)
    {
        var jamConnections = _channelMessages.GetOrAdd(channelIdentifier, static _ => new ConcurrentDictionary<string, Channel<MessageItem>>());

        if (jamConnections.TryRemove($"{userId}:{connectionIdentifier}", out var channel))
            channel.Writer.Complete();
    }

    public bool PushMessage(string channelIdentifier, string userId, string connectionIdentifier, MessageItem message)
    {
        var jamConnections = _channelMessages.GetOrAdd(channelIdentifier, static _ => new ConcurrentDictionary<string, Channel<MessageItem>>());

        if (jamConnections.TryGetValue($"{userId}:{connectionIdentifier}", out var channel))
            return channel.Writer.TryWrite(message);

        return false; // User is not connected/listening right now
    }

    private static JamChannelDetail? findChannelByIdentifier(string channelIdentifier)
    {
        foreach (var channel in _channels)
        {
            lock (channel.Value)
            {
                var foundChannel = channel.Value.FirstOrDefault(c => c.Identifier == channelIdentifier);
                if (foundChannel != null)
                    return foundChannel;
            }
        }
        return null;
    }

    [HttpPost("message/send/{channelIdentifier}")]
    public async Task<ActionResult<ApiResponseDefault>> SendMessages(string channelIdentifier, [FromBody] MessageItemRequest data, CancellationToken token)
    {
        var sender = this.GetApiPrincipal();
        if (await _userMappingCache.FindUserFromExternalGuid(data.ToUserId, token) == null)
            return BadRequest(new ApiResponseDefault(false, "No 'to' User Found"));

        if (findChannelByIdentifier(channelIdentifier) == null)
            return BadRequest(new ApiResponseBase<MessageItem[]>(false, null, "No Channel"));

        // FUTURE: Examine message type/data/size.

        var storedMessage = new MessageItem(
            data.ToUserId,
            data.ToUserConnectionIdentifier,
            sender.UserId,
            data.FromUserConnectionIdentifier,
            sender.UserProfileName,
            data.SentUtc,
            data.MessageData);

        PushMessage(channelIdentifier, data.ToUserId.ToString(), data.ToUserConnectionIdentifier, storedMessage);

        return new ApiResponseDefault();
    }

    // FUTURE: Add to DB
    private static ConcurrentDictionary<Guid, List<JamChannelDetail>> _channels = new();

    public static List<JamChannelDetail> getChannelsList(Guid user)
    {
        return _channels.GetOrAdd(user, []);
    }

    public record JamChannelDetail(
        string FriendlyName,
        string Code
    )
    {
        public string ConnectionIdentifier { get; set; } = "";
        public string Identifier { get; set; } = "";
        public Guid? HostUser { get; set; }
        public string HostUserName { get; set; } = "";
        public long? Timestamp { get; set; }
    };

    public record JamChannelDetailCreate(
        [Required]
        string ConnectionIdentifier,
        [Required]
        [StringLength(20, MinimumLength = 5, ErrorMessage = "Friendly Name must be between 1 and 50 characters.")]
        [RegularExpression("^[a-zA-Z0-9'?., ]+$", ErrorMessage = "Friendly Name must be alphanumeric and can include spaces, apostrophes, question marks, and commas.")]
        string FriendlyName,
        [Required]
        [StringLength(10, MinimumLength = 4, ErrorMessage = "Code must be between 4 and 10 characters.")]
        [RegularExpression("^[a-zA-Z0-9]+$", ErrorMessage = "Code must be alphanumeric.")]
        string Code
    );

    [HttpPost("channel/join/{channelIdentifier}/{code}/{connectionIdentifier}")]
    public ActionResult<ApiResponseBase<JamChannelDetail>> JoinChannel([Required] string channelIdentifier, [Required] string code, [Required] string connectionIdentifier, string profileName = "")
    {
        var user = this.GetApiPrincipal();

        // FUTURE: Join this user and connection to the channel. For now, just return channel details if code is correct.

        var channel = findChannelByIdentifier(channelIdentifier);
        if (channel == null)
            return BadRequest(new ApiResponseBase<JamChannelDetail>(false, null, "No Channel"));

        // The host user can join without a code, but other users must provide the correct code to join.
        if (channel.HostUser != user.UserId && channel.Code != code)
            return BadRequest(new ApiResponseBase<JamChannelDetail>(false, null, "Invalid Code"));

        return new ApiResponseBase<JamChannelDetail>(true, channel);
    }

    [HttpGet("channel/join/{channelIdentifier}/{code}/{connectionIdentifier}/events")]
    public IResult StreamNotifications(string channelIdentifier, string code, string connectionIdentifier, CancellationToken cancellationToken)
    {
        // FUTURE: Validate channelIdentifier, code, and connectionIdentifier. For now, just stream events.

        // Define an async stream using IAsyncEnumerable and SseItem
        async IAsyncEnumerable<SseItem<string>> ProduceEvents([EnumeratorCancellation] CancellationToken ct)
        {
            var userId = this.GetApiPrincipal().UserId.ToString();
            var reader = RegisterUser(channelIdentifier, userId, connectionIdentifier);

            try
            {
                // Await items as they are pushed by other callers!
                // This blocks efficiently without CPU overhead until data arrives.
                await foreach (var message in reader.ReadAllAsync(ct))
                {
                    yield return new SseItem<string>(
                        data: JsonSerializer.Serialize(message),
                        eventType: "message"
                    )
                    {
                        EventId = message.SentUtc.ToString()
                    };
                }
            }
            finally
            {
                // Ensure cleanup when the client disconnects or tab closes
                UnregisterUser(channelIdentifier, userId, connectionIdentifier);
            }
        }

        // Return the native .NET 10 ServerSentEvents result from the controller
        return TypedResults.ServerSentEvents(ProduceEvents(cancellationToken));
    }

    [HttpPost("channel/create")]
    public ActionResult<ApiResponseBase<JamChannelDetail>> CreateChannel([FromBody] JamChannelDetailCreate create)
    {
        var user = this.GetApiPrincipal();

        var userChannels = getChannelsList(user.UserId);
        lock (userChannels)
        {
            // If user already has a channel with the same friendly name, return that channel instead of creating a new one.
            var existingChannel = userChannels.FirstOrDefault(c => c.FriendlyName.Equals(create.FriendlyName, StringComparison.CurrentCultureIgnoreCase));
            if (existingChannel != null)
                return BadRequest(new ApiResponseBase<JamChannelDetail>(false, null, "Channel Name Already Exists"));

            var detail = new JamChannelDetail(
                FriendlyName: create.FriendlyName,
                Code: create.Code
            )
            {
                ConnectionIdentifier = create.ConnectionIdentifier,
                Identifier = Guid.NewGuid().ToString(),
                HostUser = user.UserId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            
            userChannels.Add(detail);

            return new ApiResponseBase<JamChannelDetail>(true, detail);
        }
    }

    [HttpDelete("channel/delete/{identifier}")]
    public ActionResult<ApiResponseDefault> DeleteChannel([Required] string identifier)
    {
        var user = this.GetApiPrincipal();

        var channel = findChannelByIdentifier(identifier);
        if (channel == null || channel.HostUser != user.UserId)
        {
            _logger.LogWarning("User {UserId} attempted to delete channel {ChannelIdentifier} but was null or not the host.", user.UserId, identifier);
            return new ApiResponseDefault();
        }

        var userChannels = getChannelsList(user.UserId);
        lock (userChannels)
        {
            userChannels.RemoveAll(x => x.Identifier.Equals(identifier, StringComparison.CurrentCultureIgnoreCase));
            _logger.LogInformation("User {UserId} deleted channel {ChannelIdentifier}.", user.UserId, identifier);
            _channelMessages.TryRemove(identifier, out _);
        }

        return new ApiResponseDefault();
    }

    /// <summary>
    /// Get Channel List of current user, and other users channels (without the code)
    /// </summary>
    [HttpGet("channel")]
    public async Task<ActionResult<ApiResponseBase<JamChannelDetail[]>>> GetChannels()
    {
        var user = this.GetApiPrincipal();

        var usernameLookup = new Dictionary<Guid, string>();

        var allChannels = new List<JamChannelDetail>();
        foreach(var channel in _channels)
        {
            usernameLookup[channel.Key] = (await _userMappingCache.FindUserFromExternalGuid(channel.Key))?.Username ?? "Unknown User";

            lock(channel.Value)
            {
                // Filter out Code for other users - so they can't see the channel code
                allChannels.AddRange(channel.Value.Select(c => new JamChannelDetail(c.FriendlyName, channel.Key == user.UserId ? c.Code : "") 
                { 
                    Identifier = c.Identifier,
                    HostUser = c.HostUser, 
                    Timestamp = c.Timestamp, 
                    HostUserName = usernameLookup[c.HostUser!.Value] 
                }));
            }
        }

        return new ApiResponseBase<JamChannelDetail[]>(true, [.. allChannels]);
    }
}
