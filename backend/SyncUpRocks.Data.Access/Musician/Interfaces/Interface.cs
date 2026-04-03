using System.Data;

namespace SyncUpRocks.Data.Access.Musician.Interfaces;

public record MusicianSetlistComplete(
    SetlistDefinition Set,
    SongDefinition[] Songs,
    TrackDefinition[] Tracks,
    FilesetDefinition[] Filesets,
    FileVersionDefinition[] LatestFileVersions);

public interface IMusicianDataAccess
{
    /// <summary>
    /// Low Level Access
    /// </summary>
    public IMusicianSetlistAccess Setlist { get; }

    /// <summary>
    /// Low Level Access
    /// </summary>
    public IMusicianSongAccess Song { get; }

    /// <summary>
    /// Low Level Access
    /// </summary>
    public IMusicianFilesetAccess Fileset { get; }

    /// <summary>
    /// Low Level Access
    /// </summary>
    public Task<(IDbConnection connection, IDbTransaction transaction)> CreateTransactionConnection();

    /// <summary>
    /// Get a complete SetList - all songs, tracks, file definitions by filesetId
    /// </summary>
    public Task<MusicianSetlistComplete?> GetSetlistComplete(long setlistId);
}

