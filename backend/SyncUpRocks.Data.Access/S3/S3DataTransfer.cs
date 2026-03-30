using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;

namespace SyncUpRocks.Data.Access.S3;

public interface IS3DataTransfer
{
    public Task UploadData(string dataStore, string bucketKey, Stream stream, string key, string contentType, Dictionary<string, string> metadata);

    public Task UploadData(FileProviderClientConfiguration providerClientConfiguration, string bucketName, Stream stream, string key, string contentType, Dictionary<string, string> metadata);

    //public Task ListBucketContents(string dataStore, string bucketKey);

    public Task<IList<string>> ListBuckets(string dataStore);
}

public class S3DataTransfer(
    ILogger<S3DataTransfer> _logger,
    IS3ClientProvider _clientProvider) : IS3DataTransfer
{
    public async Task UploadData(FileProviderClientConfiguration providerClientConfiguration, string bucketName, Stream stream, string key, string contentType, Dictionary<string, string> metadata)
    {
        var utility = new TransferUtility(providerClientConfiguration.Client);

        var request = new TransferUtilityUploadRequest
        {
            InputStream = stream,
            BucketName = bucketName,
            Key = key,
            // V4 improvement: better handling of part sizes for high-res audio
            PartSize = 6291456, // 6 MB
            ContentType = contentType
        };

        foreach (var kvp in metadata)
        {
            request.Metadata.Add(kvp.Key, kvp.Value);
        }

        _logger.LogInformation("Writing {Key} to S3 {Bucket}. Size={Size}. MetaData={MetaData}", key, request.BucketName, stream.Length, request.Metadata);

        await utility.UploadAsync(request);

    }

    public async Task UploadData(string dataStore, string bucketKey, Stream stream, string key, string contentType, Dictionary<string,string> metadata)
    {
        var providerClient = await _clientProvider.GetFileProviderClient(dataStore);
        if (!providerClient.Buckets.TryGetValue(bucketKey, out var bucketName))
        {
            _logger.LogError("Invalid bucketKey={bucketKey}", bucketKey);
            throw new Exception("Invalid Destination");
        }

        await UploadData(providerClient, bucketName, stream, key, contentType, metadata);
    }

    //public async Task ListBucketContents(string dataStore, string bucketKey)
    //{
    //    var providerClient = await _clientProvider.GetFileProviderClient(dataStore);
    //    if (!providerClient.Buckets.TryGetValue(bucketKey, out var bucketName))
    //    {
    //        _logger.LogError("Invalid bucketKey={bucketKey}", bucketKey);
    //        throw new Exception("Invalid Destination");
    //    }

    //    var request = new ListObjectsV2Request
    //    {
    //        BucketName = bucketName,
    //    };

    //    // V4 Paginator: Returns an IAsyncEnumerable
    //    var paginator = providerClient.Client.Paginators.ListObjectsV2(request);

    //    await foreach (var response in paginator.Responses)
    //    {
    //        foreach (var obj in response.S3Objects)
    //        {
    //            Console.WriteLine($"Found: {obj.Key} | Size: {obj.Size} bytes");
    //        }
    //    }
    //}

    public async Task<IList<string>> ListBuckets(string dataStore)
    {
        var providerClient = await _clientProvider.GetFileProviderClient(dataStore);

        try
        {
            // Simplest call that requires a valid connection/auth
            var result = await providerClient.Client.ListBucketsAsync();
            return [.. result.Buckets.Select(x => x.BucketName)];
        }
        catch (Exception ex)
        {
            // Log the error (e.g., S3Mock container is down)
            _logger.LogError(ex, "S3 ListBuckets Failed");
            return [];
        }
    }
}