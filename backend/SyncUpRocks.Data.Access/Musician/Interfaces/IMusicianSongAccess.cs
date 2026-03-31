using System;
using System.Data;
using System.Text.Json;
using SyncUpRocks.Data.Access.TypeHandlers;

namespace SyncUpRocks.Data.Access.Musician.Interfaces;

public class SongDefinition
{
    public long? Id { get; set; }
    public long OwnerId { get; set; }
    public string Name { get; set; } = "";
    public int DurationMilliseconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool InTrash { get; set; }
    [Jsonb]
    public Dictionary<string, object?>? Configuration { get; set; }
}

public class TrackDefinition
{
    public long? Id { get; set; }
    public long? SongId { get; set; }
    public long? FileSetId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Format { get; set; } = "";
    public int? VersionNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    [Jsonb]
    public Dictionary<string, object?>? Configuration { get; set; }
}

public interface IMusicianSongAccess
{
    Task<IList<SongDefinition>> GetSongs(long ownerId, bool includeTrash, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task SaveSongTrack(TrackDefinition songDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task SaveSong(SongDefinition songDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task<SongDefinition?> GetSong(long songId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    /// <summary>
    /// Note: Attached File Sets are marked is_deleted. Follow up jobs will purge records
    /// </summary>
    Task DeleteSong(long songId, long ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);
}