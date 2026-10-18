using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyncUpRocks.Api.Caches;
using SyncUpRocks.Api.Security;
using SyncUpRocks.Data.Access.Musician.Interfaces;
using SyncUpRocks.Data.Access.S3;
using SyncUpRocks.Data.Importers.SetList.v1;
using SyncUpRocks.Types;

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
    public record MessageItem(
        Guid ToUserId,
        Guid? FromUserId,
        string? FromUsername,
        long SentUtc,
        MessageBody MessageData
    );

    // Each channel has a queue of messages for each user. This is a simple in-memory queue for now, but will be replaced with a more robust solution in the future.
    private static ConcurrentDictionary<string, ConcurrentDictionary<Guid, List<MessageItem>>> _channelMessages = new();


    private static List<MessageItem> getMessageQueue(string channelIdentifier, Guid user)
    {
        var channelMessages = _channelMessages.GetOrAdd(channelIdentifier, _ => new ConcurrentDictionary<Guid, List<MessageItem>>());
        return channelMessages.GetOrAdd(user, []);
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

    [HttpPost("message/read/{channelIdentifier}")]
    public ActionResult<ApiResponseBase<MessageItem[]>> ReadMessages(string channelIdentifier)
    {
        if (findChannelByIdentifier(channelIdentifier) == null)
            return BadRequest(new ApiResponseBase<MessageItem[]>(false, null, "No Channel"));

        var q = getMessageQueue(channelIdentifier, this.GetApiPrincipal().UserId);
        MessageItem[] data = [];

        lock (q)
        {
            data = [.. q];
            q.Clear();
        }

        return new ApiResponseBase<MessageItem[]>(true, data, null);
    }

    [HttpPost("message/send/{channelIdentifier}")]
    public async Task<ActionResult<ApiResponseDefault>> SendMessages(string channelIdentifier, [FromBody] MessageItem data, CancellationToken token)
    {
        var sender = this.GetApiPrincipal();
        if (await _userMappingCache.FindUserFromExternalGuid(data.ToUserId, token) == null)
            return BadRequest(new ApiResponseDefault(false, "No 'to' User Found"));

        if (findChannelByIdentifier(channelIdentifier) == null)
            return BadRequest(new ApiResponseBase<MessageItem[]>(false, null, "No Channel"));

        // FUTURE: Examine message type/data/size.

        var storedMessage = new MessageItem(
            data.ToUserId,
            sender.UserId,
            sender.UserProfileName,
            data.SentUtc,
            data.MessageData);

        // FUTURE: Is from user allowed to send to toUser?

        var q = getMessageQueue(channelIdentifier, data.ToUserId);
        lock (q)
        {
            q.Add(storedMessage);
        }

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
        public string LeaderIdentifier { get; set; } = "";
        public string Identifier { get; set; } = "";
        public Guid? HostUser { get; set; }
        public string HostUserName { get; set; } = "";
        public long? Timestamp { get; set; }
    };

    public record JamChannelDetailCreate(
        [Required]
        string leaderIdentifier,
        [Required]
        [StringLength(20, MinimumLength = 5, ErrorMessage = "Friendly Name must be between 1 and 50 characters.")]
        [RegularExpression("^[a-zA-Z0-9'?., ]+$", ErrorMessage = "Friendly Name must be alphanumeric and can include spaces, apostrophes, question marks, and commas.")]
        string FriendlyName,
        [Required]
        [StringLength(10, MinimumLength = 4, ErrorMessage = "Code must be between 4 and 10 characters.")]
        [RegularExpression("^[a-zA-Z0-9]+$", ErrorMessage = "Code must be alphanumeric.")]
        string Code
    );

    /// <summary>
    /// FUTURE: Joining should open SSE/Websocket connection to receive messages. For now, just returns channel details if code is correct. And polling is required to get messages.
    /// </summary>
    /// <returns></returns>
    [HttpPost("channel/join/{channelIdentifier}/{code}")]
    public ActionResult<ApiResponseBase<JamChannelDetail>> JoinChannel([Required] string channelIdentifier, [Required] string code, string deviceName = "")
    {
        var user = this.GetApiPrincipal();

        var channel = findChannelByIdentifier(channelIdentifier);
        if (channel == null)
            return BadRequest(new ApiResponseBase<JamChannelDetail>(false, null, "No Channel"));

        // The host user can join without a code, but other users must provide the correct code to join.
        if (channel.HostUser != user.UserId && channel.Code != code)
            return BadRequest(new ApiResponseBase<JamChannelDetail>(false, null, "Invalid Code"));

        return new ApiResponseBase<JamChannelDetail>(true, channel);
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
                LeaderIdentifier = create.leaderIdentifier,
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
