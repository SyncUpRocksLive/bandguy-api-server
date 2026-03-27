
using api.Services;
using Microsoft.Extensions.Caching.Hybrid;

namespace api.Security;

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
