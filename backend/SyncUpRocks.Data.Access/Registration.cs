using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SyncUpRocks.Data.Access.Account;
using SyncUpRocks.Data.Access.HealthCheck;
using SyncUpRocks.Data.Access.Musician;
using SyncUpRocks.Data.Access.Musician.Interfaces;
using SyncUpRocks.Data.Access.S3;
using SyncUpRocks.Types;

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
        serviceCollection.AddSingleton<IS3DataTransfer, S3DataTransfer>();
        serviceCollection.AddSingleton<IS3ClientProvider, S3ClientProvider>();

        serviceCollection.TryAddEnumerable(ServiceDescriptor.Transient<IHealthCheck, DatabaseHealth>());

        return serviceCollection;
    }
}
