using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SyncUpRocks.Data.Access.Musician.Interfaces;

namespace SyncUpRocks.Data.Access.Musician;


public class MusicianSongAccess(IOptionsMonitor<ConnectionStrings> _connectionMonitor) : IMusicianSongAccess
{
    public async Task<IList<SongDefinition>> GetSongs(long ownerId, bool includeTrash, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var command = new CommandDefinition(
        @"
            SELECT 
                id AS Id,
                musician_id AS OwnerId,
                name AS Name,
                duration_ms AS DurationMilliseconds,
                created_at AS CreatedAt,
                in_trash AS InTrash,
                configuration AS Configuration
            FROM musician.songs 
            WHERE musician_id = @OwnerId 
                AND (@IncludeTrash = True OR in_trash = False);
        ",
        new { OwnerId = ownerId, IncludeTrash = includeTrash });

        if (connection != null)
            return (await connection.QueryAsync<SongDefinition>(command.CommandText, command.Parameters, transaction)).AsList();

        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        return (await conn.QueryAsync<SongDefinition>(command)).AsList();
    }

    public async Task SaveSong(SongDefinition songDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        if (songDefinition.Id == null)
        {
            var sql = @"
            INSERT INTO musician.songs (musician_id, name, duration_ms, created_at, in_trash, configuration)
                VALUES(@OwnerId, @Name, @DurationMilliseconds, @CreatedAt, @InTrash, @Configuration)
            RETURNING id;";

            var p = new { OwnerId = songDefinition.OwnerId, Name = songDefinition.Name, DurationMilliseconds = songDefinition.DurationMilliseconds, CreatedAt = songDefinition.CreatedAt, InTrash = songDefinition.InTrash, Configuration = songDefinition.Configuration };
            if (connection == null)
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                songDefinition.Id = await conn.QuerySingleAsync<long>(sql, p);
            }
            else
            {
                songDefinition.Id = await connection.QuerySingleAsync<long>(sql, p, transaction);
            }
        }
        else
        {
            var sql = @"
            UPDATE musician.songs
                SET musician_id=@OwnerId, name=@Name, duration_ms=@DurationMilliseconds, created_at=@CreatedAt, in_trash=@InTrash, configuration=@Configuration
            WHERE id = @Id;";
            var p = new { Id = songDefinition.Id, OwnerId = songDefinition.OwnerId, Name = songDefinition.Name, DurationMilliseconds = songDefinition.DurationMilliseconds, CreatedAt = songDefinition.CreatedAt, InTrash = songDefinition.InTrash, Configuration = songDefinition.Configuration };
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

    public async Task<SongDefinition?> GetSong(long songId, IDbConnection? connection, IDbTransaction? transaction)
    {
        var sql = @"
        SELECT
            id AS Id,
            musician_id AS OwnerId,
            name AS Name,
            duration_ms AS DurationMilliseconds,
            created_at AS CreatedAt,
            in_trash AS InTrash,
            configuration AS Configuration
        FROM musician.songs
        WHERE id = @Id;
        ";

        var p = new { Id = songId };
        if (connection == null)
        {
            using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
            return await conn.QuerySingleOrDefaultAsync<SongDefinition?>(sql, p);
        }
        else
        {
            return await connection.QuerySingleOrDefaultAsync<SongDefinition?>(sql, p, transaction);
        }
    }

    public async Task DeleteSong(long songId, long ownerId, IDbConnection? connection, IDbTransaction? transaction = null)
    {
        // NOTE: file sets will be left behind (intentionally)
        var command = new CommandDefinition(
            @"
                -- Purge from setlists - ensure user matches
                DELETE FROM musician.setlist_songs sls
                USING musician.setlists sl 
                    WHERE sls.setlist_id = sl.id AND sl.musician_id=@OwnerId AND sls.song_id = @Id;

                -- MARK filesets for pending deletion (external job - need to delete files)
                UPDATE musician.filesets fs
                    SET is_deleted = TRUE
                FROM musician.songs_tracks st
                    JOIN musician.songs s ON s.id = st.song_id 
                WHERE fs.id = st.fileset_id AND s.id = @Id;
                
                -- Remove Tracks
                DELETE FROM musician.songs_tracks st
                USING musician.songs s
                    WHERE st.song_id = s.id AND s.musician_id = @OwnerId AND song_id = @Id;

                -- Now - remove Song
                DELETE FROM musician.songs WHERE id = @Id AND musician_id = @OwnerId;
            ",
            new { Id = songId, OwnerId = ownerId });

        if (connection == null)
        {
            using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
            await conn.OpenAsync();
            using var trans = await conn.BeginTransactionAsync();
            await conn.ExecuteAsync(command.CommandText, command.Parameters, trans);
            trans.Commit();
        }
        else
        {
            await connection.ExecuteAsync(command.CommandText, command.Parameters, transaction);
        }
    }

    public async Task SaveSongTrack(TrackDefinition trackDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        if (trackDefinition.Id == null)
        {
            var sql = @"
            INSERT INTO musician.songs_tracks (song_id, fileset_id, name, type, format, created_at, version_number, configuration)
                VALUES(@SongId, @FilesetId, @Name, @Type, @Format, @CreatedAt, @VersionNumber, @Configuration)
            RETURNING id;";

            var p = new { SongId = trackDefinition.SongId, FilesetId = trackDefinition.FileSetId, Name = trackDefinition.Name, Type = trackDefinition.Type, Format = trackDefinition.Format, CreatedAt = trackDefinition.CreatedAt, VersionNumber = trackDefinition.VersionNumber, Configuration = trackDefinition.Configuration };
            if (connection == null)
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                trackDefinition.Id = await conn.QuerySingleAsync<long>(sql, p);
            }
            else
            {
                trackDefinition.Id = await connection.QuerySingleAsync<long>(sql, p, transaction);
            }
        }
        else
        {
            // Mark updated row with new value, then re-align all other rows to be INDEX * 10
            var sql = @"
            UPDATE musician.songs_tracks
                SET song_id=@SongId, fileset_id=@FilesetId, name=@Name, type=@Type, format=@Format, created_at=@CreatedAt, version_number=@VersionNumber, configuration=@Configuration
            WHERE id = @Id;";
            var p = new { Id = trackDefinition.Id, SongId = trackDefinition.SongId, FilesetId = trackDefinition.FileSetId, Name = trackDefinition.Name, Type = trackDefinition.Type, Format = trackDefinition.Format, CreatedAt = trackDefinition.CreatedAt, VersionNumber = trackDefinition.VersionNumber, Configuration = trackDefinition.Configuration };
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
