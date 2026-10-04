using System.Collections.Concurrent;
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

    private static ConcurrentDictionary<Guid, List<MessageItem>> _messages = new();

    public static List<MessageItem> getMessageQueue(Guid user)
    {
        return _messages.GetOrAdd(user, []);
    }

    [HttpPost("message/read")]
    public ActionResult<ApiResponseBase<MessageItem[]>> ReadMessages()
    {
        var q = getMessageQueue(this.GetApiPrincipal().UserId);
        MessageItem[] data = [];

        lock (q)
        {
            data = [.. q];
            q.Clear();
        }

        return new ApiResponseBase<MessageItem[]>(true, data, null);
    }

    [HttpPost("message/send")]
    public async Task<ActionResult<ApiResponseDefault>> SendMessages([FromBody] MessageItem data, CancellationToken token)
    {
        var sender = this.GetApiPrincipal();
        if (await _userMappingCache.FindUserFromExternalGuid(data.ToUserId, token) == null)
            return BadRequest(new ApiResponseDefault(false, "No 'to' User Found"));

        // FUTURE: Examine message type/data/size.

        var storedMessage = new MessageItem(
            data.ToUserId,
            sender.UserId,
            sender.UserProfileName,
            data.SentUtc,
            data.MessageData);

        // FUTURE: Is from user allowed to send to toUser?

        var q = getMessageQueue(data.ToUserId);
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
        string Identifier,
        string FriendlyName,
        string Code
    )
    {
        public Guid? HostUser { get; set; }
        public long? Timestamp { get; set; }
    };

    [HttpPost("channel/create")]
    public ActionResult<ApiResponseBase<JamChannelDetail>> CreateChannel([FromBody] JamChannelDetail detail)
    {
        var user = this.GetApiPrincipal();

        var userChannels = getChannelsList(user.UserId);
        lock (userChannels)
        {
            var existingChannel = userChannels.FirstOrDefault(c => c.Identifier.Equals(detail.Identifier, StringComparison.CurrentCultureIgnoreCase));
            if (existingChannel != null)
            {
                return new ApiResponseBase<JamChannelDetail>(true, existingChannel);
            }
            else
            {
                detail.HostUser = user.UserId;
                detail.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                userChannels.Add(detail);
                return new ApiResponseBase<JamChannelDetail>(true, detail);
            }
        }
    }

    [HttpDelete("channel/delete/{identifier}")]
    public ActionResult<ApiResponseDefault> DeleteChannel(string identifier)
    {
        var user = this.GetApiPrincipal();

        var userChannels = getChannelsList(user.UserId);
        lock (userChannels)
        {
            userChannels.RemoveAll(x => x.Identifier.Equals(identifier, StringComparison.CurrentCultureIgnoreCase));
        }

        return new ApiResponseDefault();
    }

    [HttpGet("channel")]
    public ActionResult<ApiResponseBase<JamChannelDetail[]>> GetChannels()
    {
        var user = this.GetApiPrincipal();

        var allChannels = new List<JamChannelDetail>();
        foreach(var channel in _channels)
        {
            lock(channel.Value)
            {
                if(channel.Key == user.UserId)
                {
                    allChannels.AddRange(channel.Value);
                }
                else
                {
                    // Fitler out Code for other users - so they can't see the channel code
                    allChannels.AddRange(channel.Value.Select(c => new JamChannelDetail(c.Identifier, c.FriendlyName, "") { HostUser = c.HostUser, Timestamp = c.Timestamp }));   
                }
            }
        }

        return new ApiResponseBase<JamChannelDetail[]>(true, [.. allChannels]);
    }
}
