using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Text;
using Amazon.Runtime.Internal.Util;
using Amazon.S3;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SyncUpRocks.Data.Access.S3;

public interface IS3DataTransfer
{
    public Task UploadData(Stream stream, string key, string contentType, Dictionary<string, string> metadata);
}

public class S3DataTransfer(
    ILogger<S3DataTransfer> _logger,
    IAmazonS3 _s3Client, 
    IOptionsMonitor<ConnectionStrings> _connectionStringMonitor) : IS3DataTransfer
{
    public async Task UploadData(Stream stream, string key, string contentType, Dictionary<string,string> metadata)
    {
        var utility = new TransferUtility(_s3Client);

        var streamLength = stream.Length;

        var response = await _s3Client.ListBucketsAsync();

        var request = new TransferUtilityUploadRequest
        {
            InputStream = stream,
            BucketName = _connectionStringMonitor.CurrentValue.SongBucket,
            Key = key,
            // V4 improvement: better handling of part sizes for high-res audio
            PartSize = 6291456, // 6 MB
            ContentType = contentType
        };

        foreach (var kvp in metadata)
        {
            request.Metadata.Add(kvp.Key, kvp.Value);
        }

        _logger.LogInformation("Writing {Key} to S3 {Bucket}. Size={Size}. MetaData={MetaData}", key, request.BucketName, streamLength, request.Metadata);

        await utility.UploadAsync(request);
    }
}