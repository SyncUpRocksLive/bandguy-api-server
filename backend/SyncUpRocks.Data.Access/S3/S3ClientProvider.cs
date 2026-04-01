using System;
using System.Collections.Frozen;
using System.Text.Json;
using System.Xml.Linq;
using Amazon.Runtime;
using Amazon.S3;
using Dapper;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace SyncUpRocks.Data.Access.S3;

public interface IS3ClientProvider
{
    public Task<FileProviderClientConfiguration?> GetFileProviderClientById(long id);

    public Task<FileProviderClientConfiguration?> GetFileProviderClient(string name);
}

public class InvalidProviderException : Exception
{
}

/// <summary>
/// Using a class here - as opposed to a record - planning to have a lock/unlock in the future.
/// If/when looking to dispose/cleanup on credential rotations.
/// </summary>
public class FileProviderClientConfiguration(
    long _id,
    AmazonS3Client _client,
    FrozenDictionary<string, string> _buckets
)
{
    public long Id => _id;
    
    public IAmazonS3 Client => _client;

    public FrozenDictionary<string, string> Buckets => _buckets;
}

public class S3ClientProvider(
    IMemoryCache _cache,
    IOptionsMonitor<ConnectionStrings> _connectionMonitor) : IS3ClientProvider
{
    public async Task<FileProviderClientConfiguration?> GetFileProviderClientById(long id)
    {
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
        };

        return await _cache.GetOrCreateAsync($"S3FileProvider-id{id}",
            async cancel =>
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                var (Id, Configuration) = await conn.QuerySingleOrDefaultAsync<(long? Id, string? Configuration)>(
                    @"SELECT
                        id as Id,
                        configuration AS Configuration
                    FROM app.file_providers 
                    WHERE id = @Id AND type = 's3'",
                    new { Id = id }
                );

                if (Id == null || Configuration == null)
                    throw new InvalidProviderException();

                // TODO: Decrypt Configuration!!!
                var dbConfig = JsonSerializer.Deserialize<S3ConfigObject>(Configuration);
                if (dbConfig == null)
                    throw new InvalidProviderException();

                var credentials = new BasicAWSCredentials(dbConfig.AccessKey, dbConfig.Secret);
                var config = new AmazonS3Config
                {
                    ServiceURL = dbConfig.ServiceURL,
                    ForcePathStyle = dbConfig.ForcePathStyle,
                    AuthenticationRegion = dbConfig.Region
                };

                return new FileProviderClientConfiguration((long)Id, new AmazonS3Client(credentials, config), dbConfig.Buckets.ToFrozenDictionary());
            }, entryOptions
        );
    }

    public async Task<FileProviderClientConfiguration?> GetFileProviderClient(string name)
    {
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
        };

        return await _cache.GetOrCreateAsync($"S3FileProvider-{name}",
            async cancel =>
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                var (Id, Configuration) = await conn.QuerySingleOrDefaultAsync<(long? Id, string? Configuration)>(
                    @"SELECT
                        id as Id,
                        configuration AS Configuration
                    FROM app.file_providers 
                    WHERE name = @Name AND type = 's3'",
                    new { Name = name }
                );

                if (Id == null || Configuration == null)
                    throw new InvalidProviderException();

                // TODO: Decrypt Configuration!!!
                var dbConfig = JsonSerializer.Deserialize<S3ConfigObject>(Configuration);
                if (dbConfig == null)
                    throw new InvalidProviderException();

                var credentials = new BasicAWSCredentials(dbConfig.AccessKey, dbConfig.Secret);
                var config = new AmazonS3Config
                {
                    ServiceURL = dbConfig.ServiceURL,
                    ForcePathStyle = dbConfig.ForcePathStyle,
                    AuthenticationRegion = dbConfig.Region
                };

                return new FileProviderClientConfiguration((long)Id, new AmazonS3Client(credentials, config), dbConfig.Buckets.ToFrozenDictionary());
            }, entryOptions
        );
    }
}

internal class S3ConfigObject
{
    public Dictionary<string, string> Buckets { get; set; } = [];
    public string? Region { get; set; }
    public string? ServiceURL { get; set; }
    public bool ForcePathStyle { get; set; } = false;
    public string? AccessKey { get; set; }
    public string? Secret { get; set; }
}

