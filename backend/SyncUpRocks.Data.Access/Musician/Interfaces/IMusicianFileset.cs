using System;
using System.Data;
using System.Text.Json;
using SyncUpRocks.Data.Access.TypeHandlers;

namespace SyncUpRocks.Data.Access.Musician.Interfaces;

public class FilesetDefinition
{
    public long? Id { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public class FileVersionDefinition
{
    public long? Id { get; set; }
    public long? FilesetId { get; set; }
    public int VersionNumber { get; set; }
    public long? FileProviderId { get; set; }
    public string FileLocation { get; set; } = ""; // e.g. s3://bucket/key
    public long FileSizeBytes{ get; set; }
    public string ContentType { get; set; } = ""; // e.g. image/jpeg, application/lyric1|2|3|etc
    public string ChecksumSha256 { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; }
}

public interface IMusicianFilesetAccess
{
    Task SaveFileset(FilesetDefinition filesetDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task<IList<FileVersionDefinition>> GetFileVersions(long filesetId, bool onlyLatest = true, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task<FilesetDefinition?> GetFilesetById(long filesetId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task<IList<FilesetDefinition>> GetFilesetsByOwner(Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);
    
    Task SaveFilesetVersion(FileVersionDefinition filesetVersionDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null);

    //Task SaveSongTrack(TrackDefinition songDefinition, IDbConnection? connection = null);

    //Task SaveSong(SongDefinition songDefinition, IDbConnection? connection = null);

    ///// <summary>
    ///// Note: Attached File Sets are marked is_deleted. Follow up jobs will purge records
    ///// </summary>
    //Task DeleteSong(long songId, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);
}