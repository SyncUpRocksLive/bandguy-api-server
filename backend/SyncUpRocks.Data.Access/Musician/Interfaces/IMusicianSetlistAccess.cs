using System.Data;

namespace SyncUpRocks.Data.Access.Musician.Interfaces;

public class SetlistDefinition
{
    public long? Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    
    /// <summary>
    /// ReadOnly - Calculated Column: Number of Songs in the setlist
    /// </summary>
    public int SongCount { get; set; }
}

public class SetlistSongDefinition
{
    public long? Id { get; set; }
    public long? SetListId { get; set; }
    public long? SongId { get; set; }
    public int SetOrder { get; set; }
}

public interface IMusicianSetlistAccess
{
    Task<IList<SetlistDefinition>> GetSetLists(Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task SaveSetlist(SetlistDefinition setlistDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task DeleteSetlist(long setlistId, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task DeleteSetlistSong(long setlistSongId, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task SaveSetlistSong(SetlistSongDefinition setlistSong, IDbConnection? connection = null, IDbTransaction? transaction = null);
}