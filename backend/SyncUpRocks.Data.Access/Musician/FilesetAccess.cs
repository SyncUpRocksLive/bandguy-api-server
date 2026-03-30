using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SyncUpRocks.Data.Access.Musician.Interfaces;

namespace SyncUpRocks.Data.Access.Musician;

public class MusicianFilesetAccess(IOptionsMonitor<ConnectionStrings> _connectionMonitor) : IMusicianFilesetAccess
{
    public async Task<IList<FileVersionDefinition>> GetFileVersions(long filesetId, bool onlyLatest, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var baseSql = @"
            SELECT 
                id AS Id,
                fileset_id AS FilesetId,
                version_number AS VersionNumber,
                file_provider_id AS FileProviderId,
                file_location AS FileLocation,
                file_size_bytes AS FileSizeBytes,
                content_type AS ContentType,
                checksum_sha256 AS ChecksumSha256,
                created_at AS CreatedAt
            FROM musician.file_versions 
            WHERE fileset_id = @FilesetId ORDER BY version_number DESC";

        if (onlyLatest)
            baseSql += " LIMIT 1;";
        else
            baseSql += ";";

        var command = new CommandDefinition(baseSql, new { FilesetId = filesetId });

        if (connection != null)
            return (await connection.QueryAsync<FileVersionDefinition>(command.CommandText, command.Parameters, transaction)).AsList();

        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        return (await conn.QueryAsync<FileVersionDefinition>(command)).AsList();
    }

    public async Task<FilesetDefinition?> GetFilesetById(long filesetId, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var command = new CommandDefinition(
        @"
            SELECT 
                id AS Id,
                musician_id AS OwnerId,
                created_at AS CreatedAt,
                is_deleted AS IsDeleted
            FROM musician.file_versions 
            WHERE fileset_id = @FilesetId;
        ", new { FilesetId = filesetId });

        if (connection != null)
            return await connection.QuerySingleAsync<FilesetDefinition>(command.CommandText, command.Parameters, transaction);

        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        return await conn.QuerySingleAsync<FilesetDefinition>(command);
    }

    public async Task<IList<FilesetDefinition>> GetFilesetsByOwner(Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var command = new CommandDefinition(
        @"
            SELECT 
                id AS Id,
                musician_id AS OwnerId,
                created_at AS CreatedAt,
                is_deleted AS IsDeleted
            FROM musician.file_versions 
            WHERE musician_id = @OwnerId::uuid;
        ", new { OwnerId = ownerId });

        if (connection != null)
            return (await connection.QueryAsync<FilesetDefinition>(command.CommandText, command.Parameters, transaction)).AsList();

        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        return (await conn.QueryAsync<FilesetDefinition>(command)).AsList();
    }

    public async Task SaveFileset(FilesetDefinition filesetDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        if (filesetDefinition.Id == null)
        {
            var sql = @"
            INSERT INTO musician.filesets (musician_id, created_at, is_deleted)
                VALUES(@OwnerId::uuid, @CreatedAt, @IsDeleted)
            RETURNING id;";

            var p = new { OwnerId = filesetDefinition.OwnerId, CreatedAt = filesetDefinition.CreatedAt, IsDeleted = filesetDefinition.IsDeleted };
            if (connection == null)
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                filesetDefinition.Id = await conn.QuerySingleAsync<long>(sql, p);
            }
            else
            {
                filesetDefinition.Id = await connection.QuerySingleAsync<long>(sql, p, transaction);
            }
        }
        else
        {
            var sql = @"
            UPDATE musician.filesets
                SET is_deleted=@IsDeleted
            WHERE id=@Id AND musician_id=@OwnerId;";
            var p = new { Id = filesetDefinition.Id, OwnerId = filesetDefinition.OwnerId, IsDeleted = filesetDefinition.IsDeleted };
            if (connection == null)
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                await conn.ExecuteAsync(sql, p);
            }
            else
            {
                await connection.ExecuteAsync(sql, p, transaction);
            }
        }
    }

    public async Task SaveFilesetVersion(FileVersionDefinition filesetVersion, IDbConnection? connection, IDbTransaction? transaction)
    {
        if (filesetVersion.Id == null)
        {
            var sql = @"
            INSERT INTO musician.file_versions (fileset_id, version_number, file_provider_id, file_location, file_size_bytes, content_type, checksum_sha256, uploaded_at)
                VALUES(@FilesetId, @VersionNumber, @FileProviderId, @FileLocation, @FileSizeBytes, @ContentType, @ChecksumSha256, @UploadedAt)
            RETURNING id;";

            var p = new {
                FilesetId = filesetVersion.FilesetId, 
                VersionNumber = filesetVersion.VersionNumber, 
                FileProviderId = filesetVersion.FileProviderId, 
                FileLocation = filesetVersion.FileLocation, 
                FileSizeBytes = filesetVersion.FileSizeBytes, 
                ContentType = filesetVersion.ContentType, 
                ChecksumSha256 = filesetVersion.ChecksumSha256, 
                UploadedAt = filesetVersion.UploadedAt 
            };
            if (connection == null)
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                filesetVersion.Id = await conn.QuerySingleAsync<long>(sql, p);
            }
            else
            {
                filesetVersion.Id = await connection.QuerySingleAsync<long>(sql, p, transaction);
            }
        }
        else
        {
            var sql = @"
            UPDATE musician.file_versions
                SET version_number=@VersionNumber, file_location=@FileLocation, content_type=@ContentType, checksum_sha256=@ChecksumSha256
            WHERE id=@Id;";
            var p = new
            {
                Id = filesetVersion.Id,
                VersionNumber = filesetVersion.VersionNumber,
                FileLocation = filesetVersion.FileLocation,
                ContentType = filesetVersion.ContentType,
                ChecksumSha256 = filesetVersion.ChecksumSha256,
            };
            if (connection == null)
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                await conn.ExecuteAsync(sql, p);
            }
            else
            {
                await connection.ExecuteAsync(sql, p, transaction);
            }
        }
    }
}
