using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SyncUpRocks.Data.Access.Musician;
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
    IS3DataTransfer _s3DataTransfer)
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

        try
        {
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
                        if (trackFileInfo.Exists)
                            track.FileInfo = trackFileInfo;
                    }

                    // Filter out missing files
                    song.Tracks = [.. song.Tracks.Where(x => x.FileInfo != null)];
                }
            }

            var (id, name) = await loadSetlist(request, setlist);
            return (true, id, name, "");
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

    private async Task<(long setlistId, string setlistName)> loadSetlist(ImportRequest request, Setlist setlist)
    {
        if (request.ResourceType != "musician")
            throw new InvalidOperationException("Unsupported resource");

        var (connection, transaction) = await _musicianDataAccess.CreateTransactionConnection();
        var setlistAccess = _musicianDataAccess.Setlist;
        var songAccess = _musicianDataAccess.Song;

        try
        {
            // TODO: Upload files to S3 before writing to database -
            foreach(var song in setlist.Songs)
            {
                foreach(var track in song.Tracks)
                {
                    using var stream = track.FileInfo.OpenRead();
                    // FUTURE: Change this
                    await _s3DataTransfer.UploadData("data-store", "song", stream, "songs/user/0/", "application/text", new() { { "key", "1" } });
                }
            }

            // Create setlist - 
            // TODO: Support setlist MODES:
            // 1. Create New On Duplicate Name (current behavior)
            // 2. Update Existing
            // 3. Fail on duplicate
            var newSetlist = new SetlistDefinition
            {
                Id = null,
                Name = setlist.Name,
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerId = request.ResourceOwnerId,
            };
            await setlistAccess.SaveSetlist(newSetlist, connection);

            // TODO: Create Songs

            // TODO: Create File Sets

            // TODO: Create Tracks

            // TODO: Create setlist songs

            transaction.Commit();

            return ((long)newSetlist.Id!, newSetlist.Name);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Failure when trying to import data to s3/database");
            // TODO: If failure, need to schedule S3 files for deletion
            throw;
        }
        finally
        {
            connection.Dispose();
        }
    }
}
