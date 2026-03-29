using System;
using System.Data;
using System.Text.Json;
using SyncUpRocks.Data.Access.TypeHandlers;

namespace SyncUpRocks.Data.Access.Musician;

public class SongDefinition
{
    public long? Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public int DurationMilliseconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool InTrash { get; set; }
    [Jsonb]
    public JsonElement? Configuration { get; set; }
}

public interface IMusicianSongAccess
{
    Task<IList<SongDefinition>> GetSongs(Guid ownerId, bool includeTrash, IDbConnection? connection = null, IDbTransaction? transaction = null);

    //Task SaveSong(SongDefinition setlistDefinition, IDbConnection? connection = null);

    /// <summary>
    /// Note: Attached File Sets are marked is_deleted. Follow up jobs will purge records
    /// </summary>
    Task DeleteSong(long songId, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);
}