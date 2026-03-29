
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace SyncUpRocks.Api.Security;

public class MemoryCacheTicketStore : ITicketStore
{
    private const string KeyPrefix = "AuthTicket-";
    private readonly IMemoryCache _cache;

    public MemoryCacheTicketStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = KeyPrefix + Guid.NewGuid().ToString();
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var options = new MemoryCacheEntryOptions();
        var expiresUtc = ticket.Properties.ExpiresUtc;

        if (expiresUtc.HasValue)
        {
            options.SetAbsoluteExpiration(expiresUtc.Value);
        }

        _cache.Set(key, ticket, options);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        _cache.TryGetValue(key, out AuthenticationTicket? ticket);
        return Task.FromResult(ticket);
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }
}