using System.Data;
using Amazon.S3.Model;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SyncUpRocks.Data.Access.Musician.Interfaces;

namespace SyncUpRocks.Data.Access.Musician;

public class MusicianSetlistAccess(IOptionsMonitor<ConnectionStrings> _connectionMonitor) : IMusicianSetlistAccess
{
    public async Task ReplaceSetlistSongs(long setlistId, long ownerId, List<SetlistSongDefinition> songs, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var command = new CommandDefinition(
            @"
                DELETE FROM musician.setlist_songs sls
                    USING musician.setlists sl
                WHERE sls.setlist_id = sl.id
                    AND sls.setlist_id = @SetlistId
                    AND sl.musician_id = @OwnerId;

                WITH inserted_songs AS (
                    SELECT @SetlistId AS setlist_id, UNNEST(@SongIds) AS song_id, UNNEST(@SetOrders) AS set_order
                ),
                filtered_songs AS (
                    SELECT inserted.setlist_id, inserted.song_id, inserted.set_order
                    FROM inserted_songs inserted
                        INNER JOIN musician.songs s ON s.id = inserted.song_id AND s.musician_id = @OwnerId
                )
                INSERT INTO musician.setlist_songs (setlist_id, song_id, set_order)
                    SELECT setlist_id, song_id, set_order from filtered_songs;
            ",
            new { SetlistId = setlistId, OwnerId = ownerId, SongIds = songs.Select(x => x.SongId).ToArray(), SetOrders = songs.Select(x => x.SetOrder).ToArray()} );

        if (connection == null)
        {
            using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
            await conn.OpenAsync();
            using var trans = await conn.BeginTransactionAsync();
            await conn.ExecuteAsync(command.CommandText, command.Parameters, trans);
            await trans.CommitAsync();
        }
        else
        {
            await connection.ExecuteAsync(command.CommandText, command.Parameters, transaction);
        }
    }

    public async Task DeleteSetlist(long setlistId, long ownerId, IDbConnection? connection, IDbTransaction? transaction = null)
    {
        // NOTE: Songs will be left behind (intentionally)
        var command = new CommandDefinition(
            @"
                DELETE FROM musician.setlist_songs sls
                    USING musician.setlists sl
                WHERE sls.setlists = @Id AND sl.musician_id = @OwnerId;

                DELETE FROM musician.setlists WHERE id = @Id AND musician_id = @OwnerId;
            ",
            new { Id = setlistId, OwnerId = ownerId });

        if (connection == null)
        {
            using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
            await conn.OpenAsync();
            using var trans = await conn.BeginTransactionAsync();
            await conn.ExecuteAsync(command.CommandText, command.Parameters, trans);
            await trans.CommitAsync();
        }
        else
        {
            await connection.ExecuteAsync(command.CommandText, command.Parameters, transaction);
        }
    }

    public async Task<IList<SetlistDefinition>> GetSetLists(long ownerId, IDbConnection? connection, IDbTransaction? transaction = null)
    {
        var command = new CommandDefinition(
        @"
            SELECT 
                id AS Id,
                musician_id AS OwnerId,
                name AS Name,
                created_at AS CreatedAt,
                (SELECT COUNT(*) FROM musician.setlist_songs ss WHERE ss.setlist_id = sl.id) AS SongCount
            FROM musician.setlists sl
            WHERE musician_id = @OwnerId;
        ",
        new { OwnerId = ownerId });

        if (connection != null)
            return (await connection.QueryAsync<SetlistDefinition>(command.CommandText, command.Parameters, transaction)).AsList();

        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        return (await conn.QueryAsync<SetlistDefinition>(command)).AsList();
    }

    public async Task<IList<SetlistSongOverview>> GetSetListsSongsOverview(long ownerId)
    {
        const string sql = @"
            SELECT 
                sl.musician_id AS OwnerId,
                sl.id AS SetlistId,
                sl.name AS SetlistName,
                sl.created_at AS SetlistCreatedAt,
                s.id AS SongId,
                s.name AS SongName,
                sls.set_order AS SongSetOrder
            FROM musician.setlists sl 
                INNER JOIN musician.setlist_songs sls ON sls.setlist_id = sl.id
                INNER JOIN musician.songs s ON s.id = sls.song_id
            WHERE sl.musician_id = @OwnerId;
            ";

        using var connection = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        return (await connection.QueryAsync<SetlistSongOverview>(sql, new { OwnerId = ownerId })).AsList();
    }

    public async Task<IList<SetlistSongOverview>> GetSetlistSongsBySetlistId(long setlistId)
    {
        const string sql = @"
            SELECT 
                sl.musician_id AS OwnerId,
                sl.id AS SetlistId,
                sl.name AS SetlistName,
                sl.created_at AS SetlistCreatedAt,
                s.id AS SongId,
                s.name AS SongName,
                sls.set_order AS SongSetOrder
            FROM musician.setlists sl 
                INNER JOIN musician.setlist_songs sls ON sls.setlist_id = sl.id
                INNER JOIN musician.songs s ON s.id = sls.song_id
            WHERE sl.id = @SetlistId;
            ";

        using var connection = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        return (await connection.QueryAsync<SetlistSongOverview>(sql, new { SetlistId = setlistId })).AsList();
    }

    public async Task SaveSetlist(SetlistDefinition setlistDefinition, IDbConnection? connection, IDbTransaction? transaction = null)
    {
        var command = new CommandDefinition(
        @"
            WITH RECURSIVE name_generator(final_name, counter) AS (
                -- Start with the base name
                SELECT CAST(@Name AS TEXT), 0
                UNION ALL
                -- Incrementally add ' - Copy X' if the previous name exists
                SELECT 
                    CAST(@Name || ' - Copy ' || (counter + 1) AS TEXT), 
                    counter + 1
                FROM name_generator
                WHERE EXISTS (
                    SELECT 1 FROM musician.setlists 
                    WHERE musician_id = @MusicianId AND name = final_name
                        AND id IS NOT NULL -- Don't conflict with yourself on update
                ) AND counter < 10
            ),
            chosen_name AS (
                -- Pick the last generated name (the one that doesn't exist or hit limit)
                SELECT final_name FROM name_generator 
                ORDER BY counter DESC LIMIT 1
            ),
            upsert AS (
                INSERT INTO musician.setlists (id, musician_id, name, created_at)
                OVERRIDING SYSTEM VALUE
                SELECT 
                    COALESCE(@Id, nextval(pg_get_serial_sequence('musician.setlists', 'id'))), 
                    @MusicianId, 
                    (SELECT final_name FROM chosen_name), 
                    @CreatedAt
                ON CONFLICT (id) DO UPDATE SET 
                    name = EXCLUDED.name
                RETURNING id, name
            )
            SELECT id, name FROM upsert;
        ",
        new { Id = setlistDefinition.Id, MusicianId = setlistDefinition.OwnerId, setlistDefinition.Name, setlistDefinition.CreatedAt });

        if (connection == null)
        {
            using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
            var (Id, Name) = await conn.QuerySingleAsync<(long Id, string Name)>(command);
            setlistDefinition.Id = Id;
            setlistDefinition.Name = Name;
        }
        else
        {
            var (Id, Name) = await connection.QuerySingleAsync<(long Id, string Name)>(command.CommandText, command.Parameters, transaction);
            setlistDefinition.Id = Id;
            setlistDefinition.Name = Name;
        }
    }

    public async Task DeleteSetlistSong(long setlistSongId, long ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var sql = @"
        DELETE FROM musician.setlist_songs sls
            USING musician.song s
        WHERE sls.id = @Id AND sls.song_id = s.id AND s.musician_id = @OwnerId;";

        var p = new { Id = setlistSongId, OwnerId = ownerId };

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

    public async Task SaveSetlistSong(SetlistSongDefinition setlistSong, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        if (setlistSong.Id == null)
        {
            var sql = @"
            INSERT INTO musician.setlist_songs (setlist_id, song_id, set_order)
                VALUES(@SetlistId, @SongId, @SetOrder)
            RETURNING id;";

            var p = new { SetlistId = setlistSong.SetListId, SongId = setlistSong.SongId, SetOrder = setlistSong.SetOrder };

            if (connection == null)
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                setlistSong.Id = await conn.QuerySingleAsync<long>(sql, p);
            }
            else
            {
                setlistSong.Id = await connection.QuerySingleAsync<long>(sql, p, transaction);
            }
        }
        else
        {
            // Mark updated row with new value, then re-align all other rows to be INDEX * 10
            var sql = @"
                UPDATE musician.setlist_songs SET set_order = @SetOrder 
                WHERE id = @Id;";
            var p = new { Id = setlistSong.Id, SetOrder = setlistSong.SetOrder };
            if (connection == null)
            {
                using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
                setlistSong.Id = await conn.QuerySingleAsync<long>(sql, p);
            }
            else
            {
                setlistSong.Id = await connection.QuerySingleAsync<long>(sql, p, transaction);
            }
        }
    }
}
