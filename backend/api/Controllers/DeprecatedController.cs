using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

/// <summary>
/// TODO: Replace this - with better, safer methds. As well as backed by postgresql datalayer
/// </summary>
[Authorize]
[ApiController]
[Route("api/legacy")]
public class DeprecatedController(
    UserMappingCache _userMappingCache) : ControllerBase
{


    #region Messages

    public record MessageBody(
        string Type,
        JsonValue Value
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
        if (await _userMappingCache.FindUserFromUserId(data.ToUserId, token) == null)
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
    #endregion

    #region channels

    private static ConcurrentDictionary<Guid, List<JamChannelDetail>> _channels = new();

    public static List<JamChannelDetail> getChannelsList(Guid user)
    {
        return _channels.GetOrAdd(user, []);
    }

    public record JamChannelDetail(
        string Identifier,
        string FriendlyName
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
        var allChannels = new List<JamChannelDetail>();
        foreach(var channel in _channels)
        {
            lock(channel.Value)
            {
                allChannels.AddRange(channel.Value);
            }
        }

        return new ApiResponseBase<JamChannelDetail[]>(true, [.. allChannels]);
    }

    #endregion

    #region sets

    public record SetSongOverview(
        long Id,
        long Version,
        string Title
    );

    public record SetOverview(
        long Id,
        string Name,
        SetSongOverview[] Songs
    );

    [HttpGet("user/sets/overview")]
    public ActionResult<ApiResponseBase<SetOverview[]>> GetSets()
    {
        return new ApiResponseBase<SetOverview[]>(true, [
            new SetOverview(1, "My Set!", [
                new SetSongOverview(1, 1, "Camp Down Races")
            ]),
            new SetOverview(2, "My Other Set!", [
                new SetSongOverview(1, 1, "Camp Down Races")
            ])
        ]);
    }

    public record Track(
        long Id,
        long SongId,
        long FileSetId,
        string Name,
        string Type,
        string Format,
        long CreatedAt,
        long VersionNumber,
        string? Configuration
    );

    public record Song(
        long Id,
        Guid OwnerId,
        string Name,
        int DurationMilliseconds,
        long CreatedAt,
        string? Configuration,

        Track[] Tracks
    );

    [HttpGet("user/song/{songId}")]
    public ActionResult<ApiResponseBase<Song>> GetSetSong(long songId)
    {
        return new ApiResponseBase<Song>(true, new Song(
            songId,
            Guid.Empty,
            "Camp Down Races",
            60000,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            null,
            [new Track(1, 1, 1, "Lead", "Vocals", "Lyric", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 1, null)]
        ));
    }

    [HttpGet("user/song/track/{trackId}/data")]
    public ActionResult GetSetSongTrack(long trackId)
    {
        string data =
@"[00:00.00] Camptown racetrack's five miles long,
[00:06.00] Oh, doo-dah day!
[00:15.00] Camptown ladies sing this song,
[00:21.00] Oh, doo-dah day!

[00:30.00] I came down there with my hat caved in,
[00:36.00] Oh, doo-dah day!
[00:45.00] I go back home with a pocket full of tin,
[00:51.00] Oh, doo-dah day!

[01:00.00] The long-tail'd filly and the big black hoss,
[01:06.00] Oh, doo-dah day!
[01:15.00] Come to a mud hole and I fall across,
[01:21.00] Oh, doo-dah day!

[01:30.00] Camptown racetrack's five miles long,
[01:36.00] Oh, doo-dah day!
[01:45.00] Camptown ladies sing this song,
[01:51.00] Oh, doo-dah day!";

        return File(UTF8Encoding.UTF8.GetBytes(data), "application/lrc");
    }

    #endregion
}
