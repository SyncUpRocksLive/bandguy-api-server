using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using SyncUpRocks.Data.Access.TypeHandlers;

namespace SyncUpRocks.Data.Access.Musician;

public class SetlistDefinition
{
    public long? Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public interface IMusicianSetlistAccess
{
    Task<IList<SetlistDefinition>> GetSetLists(Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task SaveSetlist(SetlistDefinition setlistDefinition, IDbConnection? connection = null, IDbTransaction? transaction = null);

    Task DeleteSetlist(long setlistId, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);
}