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
@"Camptown ladies sing this song, Doo-dah! doo-dah!
Camptown race-track five miles long, Oh, doo-dah day!
I come down here with my hat caved in, Doo-dah! doo-dah!
I go back home with a pocket full of tin, Oh, doo-dah day!

CHORUS
Gonna run all night!
Gonna run all day!
I'll bet my money on the bob-tail nag,
Somebody bet on the bay.

The long tail filly and the big black horse, Doo-dah! doo-dah!
They fly the track and they both cut across, Oh, doo-dah-day!
The blind horse sticken in a big mud hole, Doo-dah! doo-dah!
Can't touch bottom with a ten foot pole, Oh, doo-dah day!

CHORUS

Old muley cow come on to the track, Doo-dah! doo-dah!
The bob-tail fling her over his back, Oh, doo-dah-day!
Then fly along like a rail-road car, Doo-dah! doo-dah!
Runnin' a race with a shootin' star, Oh, doo-dah-day!

CHORUS

See them flyin' on a ten mile heat, Doo-dah! doo-dah!
Round the race track, then repeat, Oh, doo-dah-day!
I win my money on the bob-tail nag, Doo-dah!, doo-dah!
I keep my money in an old tow bag, Oh, doo-dah-day!

CHORUS";

        // TODO: musician.songs_tracks, musician.songs, musician.file_versions, musician.file_sets
        return File(UTF8Encoding.UTF8.GetBytes(data), "application/lrc");
    }

    #endregion
}
