using System.Data;

namespace SyncUpRocks.Data.Access.Musician.Interfaces;

public class SetlistDefinition
{
    public long? Id { get; set; }
    public long OwnerId { get; set; }
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

public class SetlistSongOverview
{
    public long OwnerId { get; set; }
    public long? SetlistId { get; set; }
    public string SetlistName { get; set; } = "";
    public DateTimeOffset SetlistCreatedAt { get; set; }
    public long? SongId { get; set; }
    public string SongName { get; set; } = "";
    public int SongSetOrder { get; set; }
}

public interface IMusicianSetlistAccess
{
    Task<IList<SetlistDefinition>> GetSetLists(long ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task<IList<SetlistSongOverview>> GetSetListsSongsOverview(long ownerId);

    Task<IList<SetlistSongOverview>> GetSetlistSongsBySetlistId(long setlistId);

    Task SaveSetlist(SetlistDefinition setlistDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task DeleteSetlist(long setlistId, long ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task DeleteSetlistSong(long setlistSongId, long ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task ReplaceSetlistSongs(long setlistId, long ownerId, List<SetlistSongDefinition> songs, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task SaveSetlistSong(SetlistSongDefinition setlistSong, IDbConnection? connection = null, IDbTransaction? transaction = null);
}