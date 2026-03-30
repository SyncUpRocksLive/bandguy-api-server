using System.Data;
using System.Linq;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SyncUpRocks.Data.Access.Musician.Interfaces;

namespace SyncUpRocks.Data.Access.Musician;

public class MusicianDataAccess(
    IOptionsMonitor<ConnectionStrings> _connectionMonitor) : IMusicianDataAccess
{
    public IMusicianSetlistAccess Setlist { get; } = new MusicianSetlistAccess(_connectionMonitor);

    public IMusicianSongAccess Song { get; } = new MusicianSongAccess(_connectionMonitor);

    public IMusicianFilesetAccess Fileset { get; } = new MusicianFilesetAccess(_connectionMonitor);

    public async Task<(IDbConnection connection, IDbTransaction transaction)> CreateTransactionConnection()
    {
        var connection = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        return (connection, transaction);
    }

    public async Task<MusicianSetlistComplete?> GetSetlistComplete(long setlistId)
    {
        const string sql = @"
            -- 1. The Setlist
            SELECT 
                id AS Id,
                musician_id AS OwnerId,
                name AS Name,
                created_at AS CreatedAt
            FROM musician.setlists WHERE id = @id;

            -- 2. All Songs in this setlist (via the junction table)
            SELECT 
                s.id AS Id,
                s.musician_id AS OwnerId,
                s.name AS Name,
                s.duration_ms AS DurationMilliseconds,
                s.created_at AS CreatedAt,
                s.in_trash AS InTrash,
                s.configuration AS Configuration, 
                ss.set_order AS SetOrder FROM musician.songs s
                    JOIN musician.setlist_songs ss ON s.id = ss.song_id
            WHERE ss.setlist_id = @id ORDER BY ss.set_order ASC;

            -- 3. All Tracks for those songs
            SELECT st.* FROM musician.songs_tracks st
            WHERE st.song_id IN (SELECT song_id FROM musician.setlist_songs WHERE setlist_id = @id);

            -- 4. All Filesets linked to those tracks
            SELECT fs.* FROM musician.filesets fs
            WHERE fs.id IN (
                SELECT fileset_id FROM musician.songs_tracks 
                WHERE song_id IN (SELECT song_id FROM musician.setlist_songs WHERE setlist_id = @id)
            );

            -- 5. The specific versions needed (Latest per Fileset)
            SELECT DISTINCT ON (fv.fileset_id) fv.* FROM musician.file_versions fv
            WHERE fv.fileset_id IN (
                SELECT fileset_id FROM musician.songs_tracks 
                WHERE song_id IN (SELECT song_id FROM musician.setlist_songs WHERE setlist_id = @id)
            )
            ORDER BY fv.fileset_id, fv.version_number DESC;
            ";

        using var connection = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        await connection.OpenAsync();

        // Add a transaction so our multi query has stable data
        using var transaction = await connection.BeginTransactionAsync();
        using var multi = await connection.QueryMultipleAsync(sql, new { id = setlistId }, transaction);
        transaction.Commit();

        var setlist = await multi.ReadSingleOrDefaultAsync<SetlistDefinition>();
        if (setlist == null)
            return null;

        return new MusicianSetlistComplete(
            setlist,
            [.. (await multi.ReadAsync<SongDefinition>())],
            [.. (await multi.ReadAsync<TrackDefinition>())],
            [.. (await multi.ReadAsync<FilesetDefinition>())],
            [.. (await multi.ReadAsync<FileVersionDefinition>())]
        );
    }
}
