using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SyncUpRocks.Data.Access.Musician.Interfaces;
using SyncUpRocks.Data.Access.S3;

namespace SyncUpRocks.Data.Importers.SetList.v1;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
internal class Track
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonIgnore]
    public FileInfo FileInfo { get; set; }

    [JsonIgnore]
    public FileVersionDefinition? FileVersionDefinition { get; set; } = null; 
}

internal class SongDetail
{
    [JsonPropertyName("length")]
    public string Length { get; set; } = ""; // TODO: Change to duration

    [JsonPropertyName("tracks")]
    public Track[] Tracks { get; set; } = [];
}

internal class Song
{
    [JsonPropertyName("song_path")]
    public string SongPath { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; set; }

    [JsonIgnore]
    public Track[] Tracks { get; set; } = [];
}

internal class Setlist
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("songs")]
    public Song[] Songs { get; set; } = [];

    [JsonIgnore]
    public long? Id { get; set; } = null;
}

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.


public record ImportRequest(
    string FilePath,
    string ResourceType,
    Guid ResourceOwnerId,
    bool CreateCopyOnDuplicate,
    CancellationToken ?CancellationToken
);

/// <summary>
/// Loads and parses setlist data from a zip file containing the setlist and song details. In the future - this
/// could handle various versions of setlists. based on file name and/or contents.
/// 
/// FUTURE: All files should be scanned for malware. Should also be a seperate import JOB queue - out of process
/// </summary>
public class SetlistImporter(
    ILogger<SetlistImporter> _logger,
    IMusicianDataAccess _musicianDataAccess,
    IS3DataTransfer _s3DataTransfer,
    IS3ClientProvider _s3ClientProvider)
{
    private const long MaxZipFileSize = 1_000_000;

    public async Task<(bool success, long? setlistId, string? setlistName, string failure)> TryLoadAsync(ImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
            return (false, null, null, "Invalid Input");

        // Verify file size
        var fileInfo = new FileInfo(request.FilePath);
        if (fileInfo.Length >= MaxZipFileSize)
            return (false, null, null, "Invalid Input - file too large");

        string tempFolder = Path.Combine(Path.GetTempPath(), $"setlist_{Guid.NewGuid()}");

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["FilePath"] = request.FilePath,
            ["ResourceType"] = request.ResourceType,
            ["ResourceOwnerId"] = request.ResourceOwnerId
        });

        try
        {
            _logger.LogInformation("Beginning Import...");

            // Extract zip file
            ZipFile.ExtractToDirectory(request.FilePath, tempFolder);

            // Load setlist.json
            string setlistPath = Path.Combine(tempFolder, "setlist.json");
            fileInfo = new FileInfo(setlistPath);
            if (!fileInfo.Exists || fileInfo.Length > 50_000)
                throw new InvalidOperationException("Invalid setlist.json");

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            using var setListStream = File.OpenRead(setlistPath);
            var setlist = await JsonSerializer.DeserializeAsync<Setlist>(setListStream, jsonOptions);

            if (setlist == null)
                throw new InvalidOperationException("Failed to deserialize setlist.json");

            // Load detail.json for each song
            _logger.LogInformation("Parsing Songs");
            string songsBasePath = Path.Combine(tempFolder, "songs");
            foreach (var song in setlist.Songs)
            {
                string detailPath = Path.Combine(songsBasePath, song.SongPath, "detail.json");
                fileInfo = new FileInfo(detailPath);
                if (!fileInfo.Exists || fileInfo.Length > 50_000)
                    throw new InvalidOperationException($"Error: Invalid detail.json '{song.SongPath}' at {detailPath}");

                using var detailJsonStream = File.OpenRead(detailPath);
                var songDetails = await JsonSerializer.DeserializeAsync<SongDetail>(detailJsonStream, jsonOptions);

                if (songDetails != null)
                {
                    song.Tracks = songDetails.Tracks;
                    for (var i = 0; i < song.Tracks.Length; i++)
                    {
                        var track = song.Tracks[i];
                        var trackPath = Path.Combine(songsBasePath, song.SongPath, $"{i}.lrc"); // TODO: check ext/type
                        var trackFileInfo = new FileInfo(trackPath);
                        if (trackFileInfo.Exists && trackFileInfo.Length > 0)
                            track.FileInfo = trackFileInfo;
                    }

                    // Filter out missing files
                    song.Tracks = [.. song.Tracks.Where(x => x.FileInfo != null)];
                }
            }

            await loadSetlist(request, setlist);
            return (true, setlist.Id, setlist.Name, "");
        }
        catch (JsonException ex)
        {
            return (false, null, null, $"Invalid contents Error: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return (false, null, null, $"Invalid Structure Error: {ex.Message}");
        }
        catch(Exception ex) 
        {
            _logger.LogError(ex, "Unexpected SetListIporter Error");
            return (false, null, null, $"Unexpected Error: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempFolder))
                    Directory.Delete(tempFolder, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task loadSetlist(ImportRequest request, Setlist setlist)
    {
        if (request.ResourceType != "musician")
            throw new InvalidOperationException("Unsupported resource");

        // Grab configuration - sanity check
        var provider = await _s3ClientProvider.GetFileProviderClient("data-store");
        if (!provider.Buckets.TryGetValue("song", out var songBucket))
        {
            _logger.LogWarning("No Song Bucket!");
            throw new InvalidOperationException("No bucket configuration available");
        }

        if ((await _s3DataTransfer.ListBuckets("data-store")).Count == 0)
        {
            _logger.LogWarning("No Song Bucket!");
            throw new InvalidOperationException("No buckets available");
        }

        _logger.LogInformation("Creating DbObjects");
        var filesToUpload = await createDatabaseEntities(request, setlist, provider.Id);

        _logger.LogInformation("Loading to Data Store");
        if (!await importFilesToS3(request, setlist, filesToUpload, provider, songBucket))
            _logger.LogError("Setlist {SetlistName} {SetlistId} files may not have all uploaded. ", setlist.Id, setlist.Name);
    }

    private async Task<bool> importFilesToS3(ImportRequest request, Setlist setlist, List<Track> tracks, FileProviderClientConfiguration provider, string songBucket)
    {
        bool hadError = false;

        var folderHash = BucketHash(request.ResourceOwnerId.ToByteArray(), 500);

        foreach (var track in tracks)
        {
            var filesetVersion = track.FileVersionDefinition;
            if (filesetVersion == null || filesetVersion.Id == null)
                continue;

            try
            {
                using var stream = track.FileInfo.OpenRead();

                string key = $"songs/musician/{folderHash}/year={filesetVersion.UploadedAt.Year}/dt={filesetVersion.UploadedAt:yyyyMMdd}/{filesetVersion.Id}";

                // FUTURE: set content type by format
                filesetVersion.ContentType = "application/text";
                filesetVersion.ChecksumSha256 = await GetSha256Hash(stream);
                filesetVersion.FileSizeBytes = stream.Length;
                filesetVersion.FileLocation = key;

                await _s3DataTransfer.UploadData(provider, songBucket, stream, key, filesetVersion.ContentType, new() {
                    { "owner_id", request.ResourceOwnerId.ToString() },
                    { "fileset_version_id", filesetVersion.Id.ToString() }
                });

                await _musicianDataAccess.Fileset.SaveFilesetVersion(filesetVersion);
            }
            catch (Exception ex)
            {
                hadError = true;
                _logger.LogError(ex, "Failed Uploading {FilesetVersionId}", filesetVersion.Id);
            }
        }

        return hadError;
    }

    private async Task<List<Track>> createDatabaseEntities(ImportRequest request, Setlist setlist, long fileProviderId)
    {
        var (connection, transaction) = await _musicianDataAccess.CreateTransactionConnection();
        var setlistAccess = _musicianDataAccess.Setlist;
        var songAccess = _musicianDataAccess.Song;
        var filesetAccess = _musicianDataAccess.Fileset;

        try
        {
            List<Track> filesToUpload = [];

            var newSetlist = new SetlistDefinition
            {
                Id = null,
                Name = setlist.Name,
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerId = request.ResourceOwnerId,
            };
            await setlistAccess.SaveSetlist(newSetlist, connection, transaction);

            setlist.Id = newSetlist.Id;
            setlist.Name = newSetlist.Name;
            int songSetOrder = 0;

            foreach (var song in setlist.Songs)
            {
                var songDefinition = new SongDefinition
                {
                    OwnerId = request.ResourceOwnerId,
                    Name = song.Name,
                    CreatedAt = DateTimeOffset.UtcNow,
                    DurationMilliseconds = song.DurationMs,
                    InTrash = false,
                    Configuration = null
                };

                await songAccess.SaveSong(songDefinition, connection, transaction);

                foreach (var track in song.Tracks)
                {
                    // Create File Sets - Mark Incomplete
                    var filesetDefinition = new FilesetDefinition
                    {
                        OwnerId = request.ResourceOwnerId,
                        IsDeleted = false,
                        CreatedAt = DateTimeOffset.UtcNow
                    };

                    await filesetAccess.SaveFileset(filesetDefinition, connection, transaction);

                    var filesetVersionDefinition = new FileVersionDefinition
                    {
                        // At this point - we have not yet uploaded to Storage Layer. For now, write -- indicating imcomplete data.
                        // In the future, if S3 failed - and it stays like these, the -- file sets can be deleted/removed, or replaced.
                        ContentType = "--",
                        FileProviderId = fileProviderId,
                        FileSizeBytes = track.FileInfo.Length,
                        FilesetId = filesetDefinition.Id,
                        FileLocation = "--",
                        UploadedAt = DateTimeOffset.UtcNow,
                        VersionNumber = 1,
                        ChecksumSha256 = "--"
                    };

                    await filesetAccess.SaveFilesetVersion(filesetVersionDefinition, connection, transaction);

                    // Build list of files needing to upload - after upload, definitions will get updated
                    track.FileVersionDefinition = filesetVersionDefinition;
                    filesToUpload.Add(track);

                    var trackDefintion = new TrackDefinition
                    {
                        FileSetId = filesetDefinition.Id,
                        CreatedAt = filesetDefinition.CreatedAt,
                        Type = track.Type,
                        Format = track.Format,
                        SongId = songDefinition.Id,
                        VersionNumber = 1,
                        Name = track.Name
                    };

                    await songAccess.SaveSongTrack(trackDefintion, connection, transaction);
                }

                var setlistSongDefinition = new SetlistSongDefinition
                {
                    SetListId = newSetlist.Id,
                    SongId = songDefinition.Id,
                    SetOrder = songSetOrder
                };

                songSetOrder += 10;

                await setlistAccess.SaveSetlistSong(setlistSongDefinition, connection, transaction);
            }

            transaction.Commit();

            return filesToUpload;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failure when trying to import data to database");
            throw;
        }
        finally
        {
            transaction.Dispose();
            connection.Dispose();
        }
    }

    private async Task<string> GetSha256Hash(Stream stream)
    {
        // Important: Reset stream position if it has been read before
        if (stream.CanSeek)
            stream.Position = 0;

        byte[] hashBytes = await SHA256.HashDataAsync(stream);

        stream.Position = 0;

        // Convert to a hex string
        return Convert.ToHexString(hashBytes).ToLower();
    }

    public static int BucketHash(byte [] input, int max)
    {
        // 1. Get a stable byte array from the string (or Guid)
        byte[] hashBytes = SHA256.HashData(input);

        // 2. Convert the first 4 bytes into a non-negative integer
        // Using BitConverter to get a UInt32 ensures we don't deal with negative sign bits
        uint hashInt = BitConverter.ToUInt32(hashBytes, 0);

        return (int)(hashInt % max);
    }
}
