using System.Text.Json;
using System.Collections.Frozen;
using Amazon.Runtime;
using Amazon.S3;
using Dapper;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Npgsql;

namespace SyncUpRocks.Data.Access.S3;

public interface IS3ClientProvider
{
    public Task<FileProviderClientConfiguration> GetFileProviderClient(string name);
}

public class InvalidProviderException : Exception
{
}

/// <summary>
/// Using a class here - as opposed to a record - planning to have a lock/unlock in the future.
/// If/when looking to dispose/cleanup on credential rotations.
/// </summary>
public class FileProviderClientConfiguration(
    AmazonS3Client _client,
    FrozenDictionary<string, string> _buckets
)
{
    public IAmazonS3 Client => _client;

    public FrozenDictionary<string, string> Buckets => _buckets;
}

public class S3ClientProvider(
    HybridCache _cache,
    IOptionsMonitor<ConnectionStrings> _connectionMonitor) : IS3ClientProvider
{
    public async Task<FileProviderClientConfiguration> GetFileProviderClient(string name)
    {
        return await _cache.GetOrCreateAsync($"S3FileProvider-{name}",
            async cancel =>
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                var configuration = await conn.ExecuteScalarAsync<string>(
                    @"SELECT 
                        configuration AS Configuration
                    FROM app.file_providers 
                    WHERE name = @Name AND type = 's3'",
                    new { Name = name }
                ) ?? throw new InvalidProviderException();

                // TODO: Decrypt Configuration!!!
                var dbConfig = JsonSerializer.Deserialize<S3ConfigObject>(configuration);
                if (dbConfig == null)
                    throw new InvalidProviderException();

                var credentials = new BasicAWSCredentials(dbConfig.AccessKey, dbConfig.Secret);
                var config = new AmazonS3Config
                {
                    ServiceURL = dbConfig.ServiceURL,
                    ForcePathStyle = dbConfig.ForcePathStyle,
                    AuthenticationRegion = dbConfig.Region
                };

                return new FileProviderClientConfiguration(new AmazonS3Client(credentials, config), dbConfig.Buckets.ToFrozenDictionary());
            }
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

