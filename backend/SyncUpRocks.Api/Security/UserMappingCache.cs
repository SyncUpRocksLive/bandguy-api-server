
using Microsoft.Extensions.Caching.Hybrid;
using SyncUpRocks.Data.Access.Account;

namespace SyncUpRocks.Api.Security;

public class UserMappingCache(
    HybridCache _cache,
    IUserAccountService _userAccountService)
{
    private const string KeyPrefix = "UserMapping-";

    public async Task<UserAccount?> FindUserFromUserId(Guid userId, CancellationToken token = default)
    {
        return await _cache.GetOrCreateAsync(
            $"{KeyPrefix}-{userId}",
            async cancel => await _userAccountService.GetUserById(userId, cancel),
            cancellationToken: token
        );
    }
}
