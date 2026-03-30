using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyncUpRocks.Data.Access;
using SyncUpRocks.Data.Access.Account;
using SyncUpRocks.Data.Access.Musician;
using SyncUpRocks.Data.Access.S3;
using SyncUpRocks.Data.Importers.SetList.v1;

namespace SyncUpRocks.Unit.Tests;

public class ImporterTests
{
    [Fact]
    public async Task LoadSetlistZipImport()
    {
        var connection = new ConnectionStrings { BandguyDatabase = "Host=127.0.0.1;Database=bandguy;Username=myuser;Password=mypassword" };
        var connectionMonitor = new ValueBasedOptionsMonitor<ConnectionStrings>(connection);
        var access = new MusicianDataAccess(connectionMonitor);

        var clientProvider = new S3ClientProvider(new FakeHybridCache(), connectionMonitor);
        var s3DataTransfer = new S3DataTransfer(new NullLogger<S3DataTransfer>(), clientProvider);
        var importer = new SetlistImporter(new NullLogger<SetlistImporter>(), access, s3DataTransfer, clientProvider);

        var account = new UserAccountService(new ValueBasedOptionsMonitor<ConnectionStrings>(connection), new NullLogger<UserAccountService>());
        await account.SaveUser(new UserAccount
        {
            Id = Guid.Empty,
            Username = "Test",
            Email = "",
            CreatedAt = DateTimeOffset.UtcNow,
            LastLogin = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var result = await importer.TryLoadAsync(new ImportRequest(
            "C:\\Development\\guitar\\oss\\closed_source\\songs\\setlist1\\setlist1.zip",
            "musician",
            Guid.Empty,
            true, null));

        Assert.True(result.success);

        var setLists = await access.Setlist.GetSetLists(Guid.Empty);
        Assert.NotEmpty(setLists);
        Assert.Contains(setLists, x => x.Id == (long)result.setlistId!);

        // /check access 
        await s3DataTransfer.ListBuckets("data-store");

        foreach (var setlist in setLists)
        {
            await access.Setlist.DeleteSetlist((long)setlist.Id!, Guid.Empty);
        }
    }

    [Fact]
    public async Task Test2()
    {
        var connection = new ConnectionStrings { BandguyDatabase = "Host=127.0.0.1;Database=bandguy;Username=myuser;Password=mypassword" };
        var connectionMonitor = new ValueBasedOptionsMonitor<ConnectionStrings>(connection);
        var access = new MusicianDataAccess(connectionMonitor);

        var clientProvider = new S3ClientProvider(new FakeHybridCache(), connectionMonitor);
        var s3DataTransfer = new S3DataTransfer(new NullLogger<S3DataTransfer>(), clientProvider);
        var importer = new SetlistImporter(new NullLogger<SetlistImporter>(), access, s3DataTransfer, clientProvider);


        var item = await access.Fileset.GetFilesetById(23);

    }
}

public sealed class ValueBasedOptionsMonitor<TOptions> : IOptionsMonitor<TOptions> where TOptions : class
{
    private TOptions _value;

    public ValueBasedOptionsMonitor(TOptions value) => _value = value;

    public TOptions CurrentValue => _value;

    public TOptions Get(string name) => CurrentValue;

    // Dummy implementation of OnChange for basic unit tests
    public IDisposable OnChange(Action<TOptions, string> listener) => null;
}

public class FakeHybridCache : HybridCache
{
    // Not sure if this should be static, nor do I know what to do about Tags
    // Could make public since it's a fake cache anyway, and that could be useful for testing purposes
    private readonly Dictionary<string, object?> _cache = new();

    public override async ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
      IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        bool cached = _cache.TryGetValue(key, out object? value);
        if (cached) return (T?)value!;
        _cache.Add(key, await factory(state, cancellationToken));
        return (T?)_cache[key]!;
    }

    public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
      CancellationToken cancellationToken = default)
    {
        _cache[key] = value;
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}