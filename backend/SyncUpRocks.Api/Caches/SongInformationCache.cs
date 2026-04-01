using Microsoft.Extensions.Caching.Hybrid;
using SyncUpRocks.Data.Access.Account;
using SyncUpRocks.Data.Access.Musician.Interfaces;

namespace SyncUpRocks.Api.Caches;

public class SongInformationCache(
    HybridCache _cache,
    IMusicianDataAccess _musicianDataAccess)
{
    public async Task<MusicianSetlistComplete?> GetCompleteMusicianSetlist(long setlistId, CancellationToken token = default)
    {
        var entryOptions = new HybridCacheEntryOptions
        {
            // Expiration for distributed cache (e.g., Redis)
            Expiration = TimeSpan.FromMinutes(2),
            // Expiration for local in-memory cache
            LocalCacheExpiration = TimeSpan.FromMinutes(2)
        };

        return await _cache.GetOrCreateAsync(
            $"MusicianSetlist-{setlistId}",
            async cancel => await _musicianDataAccess.GetSetlistComplete(setlistId),
            cancellationToken: token,
            options: entryOptions
        );
    }
}
