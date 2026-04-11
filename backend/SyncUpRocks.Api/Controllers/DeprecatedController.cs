using System.Collections.Concurrent;
using System.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncUpRocks.Api.Caches;
using SyncUpRocks.Api.Security;
using SyncUpRocks.Data.Access.Musician.Interfaces;
using SyncUpRocks.Data.Access.S3;
using SyncUpRocks.Data.Importers.SetList.v1;

namespace SyncUpRocks.Api.Controllers;

/// <summary>
/// TODO: Replace this - with better, safer methds. As well as backed by postgresql datalayer
/// </summary>
[Authorize]
[ApiController]
[Route("api/legacy")]
public class DeprecatedController(
    ILogger<DeprecatedController> _logger,
    UserMappingCache _userMappingCache,
    IMusicianDataAccess _musicianDataAccess,
    SongInformationCache _songInformationCache,
    IS3DataTransfer _dataTransfer,
    SetlistImporter _setListImporter) : ControllerBase
{
    #region Messages

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
        string Name,
        int SetOrder,
        int? Tracks = null,
        long? CreatedAtMsUtc = null,
        int? DurationMs = null,
        string? Configuration = null
    );

    public record SetOverview(
        long MusicianId,
        long Id,
        string Name,
        long CreatedAtMsUtc,
        SetSongOverview[] Songs
    );

    [HttpGet("user/songs/overview/{userId:Guid}")]
    public async Task<ActionResult<ApiResponseBase<SetSongOverview[]>>> GetSongs(Guid userId)
    {
        var user = await _userMappingCache.FindUserFromExternalGuid(userId);
        if (user == null)
            return BadRequest(new ApiResponseDefault(false, "Invalid User!"));

        var songs = await _musicianDataAccess.Song.GetSongs(user.Id, false);

        var remapped = songs.Where(s => !s.InTrash).Select(s => new SetSongOverview(
            s.Id!.Value,
            s.Name,
            s.SetOrder,
            null,
            s.CreatedAt.ToUnixTimeMilliseconds(),
            s.DurationMilliseconds,
            null)).ToArray();

        return new ApiResponseBase<SetSongOverview[]>(true, remapped);
    }

    [HttpGet("user/sets/overview/{userId:Guid}")]
    public async Task<ActionResult<ApiResponseBase<SetOverview[]>>> GetSets(Guid userId)
    {
        var user = await _userMappingCache.FindUserFromExternalGuid(userId);
        if (user == null)
            return BadRequest(new ApiResponseDefault(false, "Invalid User!"));

        var setlistOverview = await _musicianDataAccess.Setlist.GetSetListsSongsOverview(user.Id);
        var remapped = setlistOverview
        .GroupBy(x => x.SetlistId) // Grouping by ID is fastest
        .Select(group => {
            // Grab the first row for the Setlist metadata
            var first = group.First();

            return new SetOverview(
                MusicianId: first.OwnerId,
                Id: first.SetlistId!.Value,
                Name: first.SetlistName,
                // Using Milliseconds for seamless Svelte/JS Date interop
                CreatedAtMsUtc: first.SetlistCreatedAt.ToUnixTimeMilliseconds(),
                Songs: group
                    .OrderBy(s => s.SongSetOrder)
                    .Where(s => s.SongId.HasValue)
                    .Select(s => new SetSongOverview(
                        Id: s.SongId!.Value,
                        Name: s.SongName,
                        SetOrder: s.SongSetOrder
                    ))
                    .ToArray()
            );
        })
        .ToArray();

        return new ApiResponseBase<SetOverview[]>(true, remapped);
    }

    /// <summary>
    /// Create or rename a setlist
    /// </summary>
    /// <returns></returns>
    [HttpPost("user/sets/save")]
    public async Task<ActionResult<ApiResponseBase<SetOverview>>> SaveSetlist(long? setlistId, string setlistName)
    {
        var currentuser = this.GetApiPrincipal();
        var user = await _userMappingCache.FindUserFromExternalGuid(currentuser.UserId);
        if (user == null)
            return BadRequest(new ApiResponseDefault(false, "Invalid User!"));

        if (string.IsNullOrWhiteSpace(setlistName))
            return BadRequest(new ApiResponseDefault(false, "Invalid Setlist Name"));

        // TODO: Catch any contraint naming errors...
        var saveDto = new SetlistDefinition
        {
            Id = setlistId,
            OwnerId = user.Id,
            Name = setlistName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _musicianDataAccess.Setlist.SaveSetlist(saveDto);

        return Ok(new ApiResponseBase<SetOverview>(true, new SetOverview(saveDto.OwnerId, saveDto.Id!.Value, saveDto.Name, saveDto.CreatedAt.ToUnixTimeMilliseconds(), [])));
    }

    [HttpDelete("user/sets/delete/{setlistId}")]
    public async Task<ActionResult<ApiResponseBase<SetOverview[]>>> DeleteSetlist(long setlistId)
    {
        var currentuser = this.GetApiPrincipal();
        var user = await _userMappingCache.FindUserFromExternalGuid(currentuser.UserId);
        if (user == null)
            return BadRequest(new ApiResponseDefault(false, "Invalid User!"));

        await _musicianDataAccess.Setlist.DeleteSetlist(setlistId, user.Id);
        return Ok(new ApiResponseDefault(true));
    }

    [HttpDelete("user/songs/delete/{songId}")]
    public async Task<ActionResult<ApiResponseBase<ApiResponseDefault>>> DeleteSong(long songId)
    {
        var currentuser = this.GetApiPrincipal();
        var user = await _userMappingCache.FindUserFromExternalGuid(currentuser.UserId);
        if (user == null)
            return BadRequest(new ApiResponseDefault(false, "Invalid User!"));

        await _musicianDataAccess.Song.PutSongToTrash(songId, user.Id);
        return Ok(new ApiResponseDefault(true));
    }

    public record SongSaveRequest(
        long? Id,
        string Name,
        int DurationMilliseconds,
        long CreatedAtMsUtc,
        string? Configuration
    );

    [HttpPost("user/songs/save")]
    public async Task<ActionResult<ApiResponseBase<SongSaveRequest>>> SaveSong([FromBody] SongSaveRequest song)
    {
        var currentuser = this.GetApiPrincipal();
        var user = await _userMappingCache.FindUserFromExternalGuid(currentuser.UserId);
        if (user == null)
            return BadRequest(new ApiResponseDefault(false, "Invalid User!"));

        var newDto = new SongDefinition
        {
            Id = song.Id,
            Name = song.Name,
            DurationMilliseconds = song.DurationMilliseconds,
            OwnerId = user.Id,
            Configuration = !string.IsNullOrWhiteSpace(song.Configuration) ? JsonSerializer.Deserialize<Dictionary<string, object?>>(song.Configuration) : null
        };
        await _musicianDataAccess.Song.SaveSong(newDto);
        return Ok(new ApiResponseBase<SongSaveRequest>(true, new SongSaveRequest(
            newDto.Id,
            newDto.Name,
            newDto.DurationMilliseconds,
            newDto.CreatedAt.ToUnixTimeMilliseconds(),
            song.Configuration
        )));
    }

    [HttpPost("user/sets/overview/save/")]
    public async Task<ActionResult<ApiResponseBase<ApiResponseDefault>>> SaveSetlistSongs(long setlistId, [FromBody] SetSongOverview[] songs)
    {
        var currentuser = this.GetApiPrincipal();
        var user = await _userMappingCache.FindUserFromExternalGuid(currentuser.UserId);
        if (user == null)
            return BadRequest(new ApiResponseDefault(false, "Invalid User!"));

        if (songs.Length == 0)
            return BadRequest(new ApiResponseDefault(false, "Cannot Remove all objects from Setlist"));

        if (songs.Length > 100)
            return BadRequest(new ApiResponseDefault(false, "Too Many Songs"));

        if (!songs.All(s => s.Id > 0))
            return BadRequest(new ApiResponseDefault(false, "Songs add to set must exist already"));

        var existingSet = await _musicianDataAccess.Setlist.GetSetlistSongsBySetlistId(setlistId);
        if (existingSet.Count == 0)
            return NotFound(new ApiResponseDefault(false, "Setlist not found"));

        if (!existingSet.All(s => s.OwnerId == user.Id))
            return Unauthorized(new ApiResponseDefault(false, "Cannot Edit Other User Setlists"));

        // Ensure all IDs are valid songs, owned by current user. Shortcut - load all user songs
        // FUTURE: Postgres supports ARRAYS! Should use ANY(@Ids) in sql query.
        var userSongIds = (await _musicianDataAccess.Song.GetSongs(user.Id, false)).Select(us => us.Id).ToHashSet();
        if (!songs.All(s => userSongIds.Contains(s.Id)))
            return BadRequest(new ApiResponseDefault(false, "Invalid Song List"));

        // TODO: Change replace implementation to update/insert/delete vs drop and replace
        await _musicianDataAccess.Setlist.ReplaceSetlistSongs(setlistId, user.Id, [.. songs.Select(s => new SetlistSongDefinition { SongId = s.Id, SetOrder = s.SetOrder })]);

        return Ok(new ApiResponseDefault(true));
    }

    public record Track(     
        long Id,
        long SongId,
        long FileSetId,
        string Name,
        string Type,
        string Format,
        long CreatedAtMsUtc,
        long VersionNumber,
        string? Configuration
    );

    public record Song(
        long Id,
        long MusicianId,
        string Name,
        int DurationMilliseconds,
        long CreatedAtMsUtc,
        int SetOrder,
        string? Configuration,
        Track[] Tracks
    );

    public record SetComplete(
        long MusicianId,
        long Id,
        string Name,
        long CreatedAtMsUtc,
        Song[] Songs
    );


    [HttpGet("user/sets/complete/{setlistId}")]
    public async Task<ActionResult<ApiResponseBase<SetComplete>>> GetCompleteSets(long setlistId)
    {
        var data = await _songInformationCache.GetCompleteMusicianSetlist(setlistId);
        if (data == null)
            return NotFound(new ApiResponseDefault(false, $"No setlist found with id='{setlistId}'"));

        // 1. Create a lookup for Tracks grouped by SongId for O(n) speed
        var tracksBySong = data.Tracks
            .Where(t => t.SongId.HasValue)
            .ToLookup(t => t.SongId!.Value);

        // 2. Project the Songs and nest their respective Tracks
        var songDtos = data.Songs
            .Where(s => s.Id.HasValue)
            .Select(s => new Song(
                Id: s.Id!.Value,
                MusicianId: data.Set.OwnerId,
                Name: s.Name,
                DurationMilliseconds: s.DurationMilliseconds,
                CreatedAtMsUtc: s.CreatedAt.ToUnixTimeMilliseconds(),
                SetOrder: s.SetOrder,
                Configuration: s.Configuration != null ? JsonSerializer.Serialize(s.Configuration) : null,
                Tracks: tracksBySong[s.Id!.Value]
                    .Select(t => new Track(
                        Id: t.Id ?? 0,
                        SongId: t.SongId ?? 0,
                        FileSetId: t.FileSetId ?? 0,
                        Name: t.Name,
                        Type: t.Type,
                        Format: t.Format,
                        CreatedAtMsUtc: t.CreatedAt.ToUnixTimeMilliseconds(),
                        VersionNumber: t.VersionNumber ?? 0,
                        Configuration: t.Configuration != null ? JsonSerializer.Serialize(t.Configuration) : null
                    ))
                    .ToArray()
            ))
            .ToArray();

        // 3. Assemble the final SetComplete record
        return new ApiResponseBase<SetComplete>(true, new SetComplete(
            MusicianId: data.Set.OwnerId,
            Id: data.Set.Id!.Value,
            Name: data.Set.Name,
            CreatedAtMsUtc: data.Set.CreatedAt.ToUnixTimeMilliseconds(),
            Songs: songDtos
        ));
    }

    [HttpGet("user/songs/complete/{songId}")]
    public async Task<ActionResult<ApiResponseBase<Song>>> GetCompleteSong(long songId)
    {
        var data = await _musicianDataAccess.GetSongComplete(songId);
        if (data == null)
            return NotFound(new ApiResponseDefault(false, $"No song found with id='{songId}'"));

        // 1. Create a lookup for Tracks grouped by SongId for O(n) speed
        var tracksBySong = data.Tracks
            .Where(t => t.SongId.HasValue)
            .ToLookup(t => t.SongId!.Value);

        // 2. Project the Songs and nest their respective Tracks
        var songDto = new Song(
                Id: data.Song.Id!.Value,
                MusicianId: data.Song.OwnerId,
                Name: data.Song.Name,
                DurationMilliseconds: data.Song.DurationMilliseconds,
                CreatedAtMsUtc: data.Song.CreatedAt.ToUnixTimeMilliseconds(),
                SetOrder: 0,
                Configuration: data.Song.Configuration != null ? JsonSerializer.Serialize(data.Song.Configuration) : null,
                Tracks: tracksBySong[data.Song.Id!.Value]
                    .Select(t => new Track(
                        Id: t.Id ?? 0,
                        SongId: t.SongId ?? 0,
                        FileSetId: t.FileSetId ?? 0,
                        Name: t.Name,
                        Type: t.Type,
                        Format: t.Format,
                        CreatedAtMsUtc: t.CreatedAt.ToUnixTimeMilliseconds(),
                        VersionNumber: t.VersionNumber ?? 0,
                        Configuration: t.Configuration != null ? JsonSerializer.Serialize(t.Configuration) : null
                    ))
                    .ToArray()
            );

        // 3. Assemble the final SetComplete record
        return new ApiResponseBase<Song>(true, songDto);
    }

    [HttpGet("user/song/track/{setlistId}/{trackId}/data")]
    public async Task<ActionResult> GetSetSongTrack(long setlistId, long trackId, CancellationToken token)
    {
        var setlist = await _songInformationCache.GetCompleteMusicianSetlist(setlistId);
        if (setlist == null)
            return NotFound(new ApiResponseDefault(false, $"No setlist found with id='{setlistId}'"));

        // FUTURE - should we cache recent files on disk?

        var track = setlist.LatestFileVersions.FirstOrDefault(t => t.Id == trackId);
        if (track == null)
            return NotFound(new ApiResponseDefault(false, $"No track found with id='{trackId}'"));

        var stream = await _dataTransfer.GetDataStream(track.FileProviderId!.Value, track.FileLocation);

        //return File(UTF8Encoding.UTF8.GetBytes(data), "application/lrc");
        return File(stream, "application/lrc");
    }

    #endregion

    const long MaxFileSize = 1 * 1024 * 1024;

    [HttpPost("user/setslist/import")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> UserImportSetlist(IFormFile file)
    {
        var apiUser = this.GetApiPrincipal();
        var localUser = await _userMappingCache.FindUserFromExternalGuid(apiUser.UserId);
        if (localUser == null)
            return Forbid("Invalid User");

        const long MaxFileSize = 1 * 1024 * 1024;
        // 1. Basic validation
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        // 2. Strict Size Check (Redundant if attribute is used, but good for safety)
        if (file.Length > MaxFileSize)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, "File exceeds 1MB limit.");

        // 3. Extension Check
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".zip")
            return BadRequest("Only .zip files are allowed.");

        try
        {
            // TODO: handle decompress as stream - all the way down.
            string tempFilePath = Path.GetTempFileName();
            using (var uploadStream = file.OpenReadStream())
            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
            {
                // Efficiently copy the upload contents to the local temp file
                await uploadStream.CopyToAsync(fileStream);
            }

            var request = new ImportRequest(tempFilePath, "musician", localUser.Id, true, default);
            var result = await _setListImporter.TryLoadAsync(request);
            
            // TODO: Return ID / NAME
            if (result.success)
                return Ok(new { message = "Playlist imported successfully" });

            return BadRequest($"Failed: {result.failure}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to import setlist");
            return StatusCode(500, "Internal server error during import.");
        }
    }
}
