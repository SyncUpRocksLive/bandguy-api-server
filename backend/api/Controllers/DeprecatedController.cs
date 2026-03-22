using System.Collections.Concurrent;
using api.Controllers.User.Models.v1;
using Microsoft.AspNetCore.Mvc;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        string Id,
        long Version,
        string Title
    );

    public record Set(
        string Id,
        string Name,
        SetSongOverview[] Songs
    );

    [HttpGet("sets")]
    public ActionResult<ApiResponseBase<Set[]>> GetSets()
    {
        return new ApiResponseBase<Set[]>(true, [
            new Set("set1", "My Set!", [
                new SetSongOverview("song1", 1, "Camp Down Races")
            ]),
            new Set("set2", "My Other Set!", [
                new SetSongOverview("song1", 1, "Camp Down Races")
            ])
        ]);
    }

    #endregion
}
