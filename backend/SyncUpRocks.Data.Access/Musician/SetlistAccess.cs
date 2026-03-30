using System.Data;
using System.Transactions;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SyncUpRocks.Data.Access.Musician.Interfaces;

namespace SyncUpRocks.Data.Access.Musician;

public class MusicianSetlistAccess(IOptionsMonitor<ConnectionStrings> _connectionMonitor) : IMusicianSetlistAccess
{
    public async Task DeleteSetlist(long setlistId, Guid ownerId, IDbConnection? connection, IDbTransaction? transaction = null)
    {
        // NOTE: Songs will be left behind (intentionally)
        var command = new CommandDefinition(
            @"
                DELETE FROM musician.setlist_songs WHERE setlist_id = @Id;
                DELETE FROM musician.setlists WHERE id = @Id AND musician_id = @OwnerId::uuid;
            ",
            new { Id = setlistId, OwnerId = ownerId });

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

    public async Task<IList<SetlistDefinition>> GetSetLists(Guid ownerId, IDbConnection? connection, IDbTransaction? transaction = null)
    {
        var command = new CommandDefinition(
        @"
            SELECT 
                id AS Id,
                musician_id AS OwnerId,
                name AS Name,
                created_at AS CreatedAt,
                (SELECT COUNT(*) FROM musician.setlist_songs ss WHERE ss.setlist_id = s.id) AS SongCount
            FROM musician.setlists 
            WHERE musician_id = @OwnerId::uuid;
        ",
        new { OwnerId = ownerId });

        if (connection != null)
            return (await connection.QueryAsync<SetlistDefinition>(command.CommandText, command.Parameters, transaction)).AsList();

        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        return (await conn.QueryAsync<SetlistDefinition>(command)).AsList();
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

    public async Task UpdateSetlistSongSortOrders(Guid setlistId, List<long> songIds, IDbConnection connection)
    {
        // We pass two arrays: one for the IDs and one for the new sort order (index)
        var sql = @"
        UPDATE musician.setlist_songs AS s
        SET sort_order = new_order
        FROM (
            SELECT 
                unnest(@Ids) AS id, 
                generate_series(1, array_length(@Ids, 1)) AS new_order
        ) AS val
        WHERE s.song_id = val.id 
          AND s.setlist_id = @SetlistId;";

        await connection.ExecuteAsync(sql, new
        {
            SetlistId = setlistId,
            Ids = songIds.ToArray()
        });
    }

    public async Task DeleteSetlistSong(long setlistSongId, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        throw new NotImplementedException();
    }

    public async Task SaveSetlistSong(SetlistSongDefinition setlistSong, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        throw new NotImplementedException();
    }
}
