using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SyncUpRocks.Data.Access.Musician.Interfaces;

namespace SyncUpRocks.Data.Access.Musician;


public class MusicianSongAccess(IOptionsMonitor<ConnectionStrings> _connectionMonitor) : IMusicianSongAccess
{
    public async Task<IList<SongDefinition>> GetSongs(Guid ownerId, bool includeTrash, IDbConnection? connection = null, IDbTransaction? transaction = null)
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
            WHERE musician_id = @OwnerId::uuid 
                AND (@IncludeTrash = True OR in_trash = False);
        ",
        new { OwnerId = ownerId, IncludeTrash = includeTrash });

        if (connection != null)
            return (await connection.QueryAsync<SongDefinition>(command.CommandText, command.Parameters, transaction)).AsList();

        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        return (await conn.QueryAsync<SongDefinition>(command)).AsList();
    }

    public async Task DeleteSong(long songId, Guid ownerId, IDbConnection? connection, IDbTransaction? transaction = null)
    {
        // NOTE: file sets will be left behind (intentionally)
        var command = new CommandDefinition(
            @"
                -- Purge from setlists - ensure user matches
                DELETE FROM musician.setlist_songs sls
                USING musician.setlists sl 
                    WHERE sls.setlist_id = sl.id AND sl.musician_id=@OwnerId::uuid AND sls.song_id = @Id;

                -- MARK filesets for pending deletion (external job - need to delete files)
                UPDATE musician.filesets fs
                    SET is_deleted = TRUE
                FROM musician.songs_tracks st
                    JOIN musician.songs s ON s.id = st.song_id 
                WHERE fs.id = st.fileset_id AND s.id = @Id;
                
                -- Remove Tracks
                DELETE FROM musician.songs_tracks st
                USING musician.songs s
                    WHERE st.song_id = s.id AND s.musician_id = @OwnerId::uuid AND song_id = @Id;

                -- Now - remove Song
                DELETE FROM musician.songs WHERE id = @Id AND musician_id = @OwnerId::uuid;
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
}
