using System.Data;

namespace SyncUpRocks.Data.Access.Musician;


public interface IMusicianDataAccess
{
    public IMusicianSetlistAccess Setlist { get; }

    public IMusicianSongAccess Song { get; }

    public Task<(IDbConnection connection, IDbTransaction transaction)> CreateTransactionConnection();
}

