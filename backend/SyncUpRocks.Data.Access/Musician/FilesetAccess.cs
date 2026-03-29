using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using Amazon.S3.Model;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SyncUpRocks.Data.Access.Musician.Interfaces;

namespace SyncUpRocks.Data.Access.Musician;

public class MusicianFilesetAccess(IOptionsMonitor<ConnectionStrings> _connectionMonitor) : IMusicianFilesetAccess
{
    public Task SaveFileset(FilesetDefinition filesetDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        throw new NotImplementedException();
    }

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
}
