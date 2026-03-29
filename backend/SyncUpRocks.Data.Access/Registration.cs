using Microsoft.Extensions.DependencyInjection;
using SyncUpRocks.Data.Access.Account;
using SyncUpRocks.Data.Access.Musician;

namespace SyncUpRocks.Data.Access;
public static class Registration
{
    /// <summary>
    /// Register: IUserAccountService, IMusicianDataAccess DI
    /// </summary>
    public static IServiceCollection AddSyncUpRocksDataAccess(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IMusicianDataAccess, MusicianDataAccess>();
        serviceCollection.AddSingleton<IUserAccountService, UserAccountService>();

        return serviceCollection;
    }
}
