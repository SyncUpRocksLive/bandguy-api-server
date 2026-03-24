using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

/// <summary>
/// TODO: Replace this - with better, safer methds. As well as backed by postgresql datalayer
/// </summary>
[ApiController]
[Route("api/legacy")]
public class DeprecatedController : ControllerBase
{
    #region Messages
    private static ConcurrentDictionary<string, List<string>> _messages = new();

    public static List<string> getMessageQueue(string user)
    {
        return _messages.GetOrAdd(user, new List<string>());
    }

    [HttpPost("message/read")]
    public ActionResult<ApiResponseBase<string[]>> ReadMessages()
    {
        // TODO: grab user from session
        var q = getMessageQueue("todo");
        string[] data = [];

        lock (q)
        {
            data = q.ToArray();
            q.Clear();
        }

        return new ApiResponseBase<string[]>(true, data, null);
    }

    [HttpPost("message/send/{to}")]
    public ActionResult<ApiResponseDefault> SendMessages(string to, [FromBody] string data)
    {
        var q = getMessageQueue(to);
        lock (q)
        {
            q.Add(data);
        }

        return new ApiResponseDefault();
    }
    #endregion

    #region channels

    private static ConcurrentDictionary<string, List<JamChannelDetail>> _channels = new();

    public static List<JamChannelDetail> getChannelsList(string user)
    {
        return _channels.GetOrAdd(user, []);
    }

    public record JamChannelDetail(
        string HostUser,
        string Identifier,
        string FriendlyName
    )
    {
        public long Timestamp { get; set; }
    };

    [HttpPost("channel/create/{name}")]
    public ActionResult<ApiResponseBase<JamChannelDetail>> CreateChannel(string name, [FromBody] JamChannelDetail detail)
    {
        var userChannels = getChannelsList(name);
        lock (userChannels)
        {
            var existingChannel = userChannels.FirstOrDefault(c => c.Identifier.Equals(detail.Identifier, StringComparison.CurrentCultureIgnoreCase));
            if (existingChannel != null)
            {
                return new ApiResponseBase<JamChannelDetail>(true, existingChannel);
            }
            else
            {
                detail.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                userChannels.Add(detail);
                return new ApiResponseBase<JamChannelDetail>(true, detail);
            }
        }
    }

    [HttpDelete("channel/delete/{name}/{identifier}")]
    public ActionResult<ApiResponseDefault> DeleteChannel(string name, string identifier)
    {
        var userChannels = getChannelsList(name);
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
        // TODO: read musician.set_lists
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
        string OwnerId,
        string Name,
        int DurationMilliseconds,
        long CreatedAt,
        string? Configuration,

        Track[] Tracks
    );

    [HttpGet("user/song/{songId}")]
    public ActionResult<ApiResponseBase<Song>> GetSetSong(long songId)
    {
        // TODO: musician.songs
        return new ApiResponseBase<Song>(true, new Song(
            songId,
            "1",
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

        // TODO: musician.songs_tracks, musician.songs, musician.file_versions, musician.file_sets
        return File(UTF8Encoding.UTF8.GetBytes(data), "application/lrc");
    }

    #endregion
}
