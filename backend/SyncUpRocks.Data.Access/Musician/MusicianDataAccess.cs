using System.Data;
using Microsoft.Extensions.Options;
using Npgsql;

namespace SyncUpRocks.Data.Access.Musician;

public class MusicianDataAccess(
    IOptionsMonitor<ConnectionStrings> _connectionMonitor) : IMusicianDataAccess
{
    public IMusicianSetlistAccess Setlist { get; } = new MusicianSetlistAccess(_connectionMonitor);

    public async Task<(IDbConnection connection, IDbTransaction transaction)> CreateTransactionConnection()
    {
        var connection = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        return (connection, transaction);
    }
}
