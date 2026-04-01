
using Microsoft.Extensions.Caching.Hybrid;
using SyncUpRocks.Data.Access.Account;

namespace SyncUpRocks.Api.Caches;

public class UserMappingCache(
    HybridCache _cache,
    IUserAccountService _userAccountService)
{
    private const string KeyPrefix = "UserMapping-";

    public async Task<UserAccount?> FindUserFromExternalGuid(Guid userId, CancellationToken token = default)
    {
        return await _cache.GetOrCreateAsync(
            $"{KeyPrefix}-{userId}",
            async cancel => await _userAccountService.GetUserByExternalUuid(userId, cancel),
            cancellationToken: token
        );
    }
}
