using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyncUpRocks.Data.Access;
using SyncUpRocks.Data.Access.Account;
using SyncUpRocks.Data.Access.Musician;
using SyncUpRocks.Data.Access.Musician.Interfaces;
using SyncUpRocks.Data.Access.S3;
using SyncUpRocks.Data.Importers.SetList.v1;
using Xunit;

namespace SyncUpRocks.Unit.Tests;

public class ImporterTests(DataAccessFixture _dataAccess) : IClassFixture<DataAccessFixture>
{
    [Fact]
    public async Task LoadSetlistZipImport()
    {
        var importer = new SetlistImporter(new NullLogger<SetlistImporter>(), _dataAccess.MusicianDataAccess, _dataAccess.S3DataTransfer, _dataAccess.S3ClientProvider);

        var longTestUserId = await _dataAccess.GetOrCreateUser(Guid.Empty);

        await _dataAccess.CleanUserSetlist(longTestUserId);

        var result = await importer.TryLoadAsync(new ImportRequest(
            "C:\\Development\\guitar\\oss\\closed_source\\songs\\setlist1\\setlist1.zip",
            "musician",
            longTestUserId,
            true, null));

        Assert.True(result.success);
        Assert.NotNull(result.setlistId);

        var setLists = await _dataAccess.MusicianDataAccess.Setlist.GetSetLists(longTestUserId);
        Assert.NotEmpty(setLists);
        Assert.Contains(setLists, x => x.Id == (long)result.setlistId!);

        var completeSetlist = await _dataAccess.MusicianDataAccess.GetSetlistComplete((long)result.setlistId);
    }

    [Fact]
    public async Task WriteAndLoadJsonb()
    {
        var longTestUserId = await _dataAccess.GetOrCreateUser(Guid.Empty);

        var connectionMonitor = new ValueBasedOptionsMonitor<ConnectionStrings>(_dataAccess.ConnectionStrings);
        var access = new MusicianDataAccess(connectionMonitor);

        // TODO: Would want to ensure that User created
        var d = new SongDefinition
        {
            CreatedAt = DateTimeOffset.UtcNow,
            DurationMilliseconds = 1,
            InTrash = false,
            OwnerId = longTestUserId,
            Name = Guid.NewGuid().ToString()
        };

        await access.Song.SaveSong(d);

        Assert.NotNull(d.Id);

        var found = await access.Song.GetSong((long)d.Id);

        Assert.NotNull(found);
        Assert.Null(found.Configuration);

        d.Configuration = new Dictionary<string, object?> { { "F", 1 } };

        await access.Song.SaveSong(d);
        found = await access.Song.GetSong((long)d.Id);
        Assert.NotNull(found);
        Assert.NotNull(found.Configuration);
    }
}

public sealed class ValueBasedOptionsMonitor<TOptions> : IOptionsMonitor<TOptions> where TOptions : class
{
    private TOptions _value;

    public ValueBasedOptionsMonitor(TOptions value) => _value = value;

    public TOptions CurrentValue => _value;

#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
    public TOptions Get(string name)
#pragma warning restore CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
    {
        ArgumentNullException.ThrowIfNull(name);
        return CurrentValue;
    }

    // Dummy implementation of OnChange for basic unit tests
#pragma warning disable CS8603 // Possible null reference return.
    public IDisposable OnChange(Action<TOptions, string> listener) => null;
#pragma warning restore CS8603 // Possible null reference return.
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

public class DataAccessFixture
{
    public ConnectionStrings ConnectionStrings => new() { BandguyDatabase = "Host=127.0.0.1;Database=bandguy;Username=myuser;Password=mypassword" };

    public MusicianDataAccess MusicianDataAccess { get; private set; }

    public S3ClientProvider S3ClientProvider { get; private set; }

    public S3DataTransfer S3DataTransfer {  get; private set; }

    public DataAccessFixture()
    {
        // Setup: Initialize data in the test database
        var connectionMonitor = new ValueBasedOptionsMonitor<ConnectionStrings>(ConnectionStrings);
        MusicianDataAccess = new MusicianDataAccess(connectionMonitor);

        S3ClientProvider = new S3ClientProvider(new FakeHybridCache(), connectionMonitor);
        S3DataTransfer = new S3DataTransfer(new NullLogger<S3DataTransfer>(), S3ClientProvider);
    }

    public async Task<long> GetOrCreateUser(Guid userGuid)
    {
        var account = new UserAccountService(new ValueBasedOptionsMonitor<ConnectionStrings>(ConnectionStrings), new NullLogger<UserAccountService>());

        var existingUser = await account.GetUserByExternalUuid(userGuid);
        if (existingUser != null)
            return existingUser.Id;

        var testUser = new UserAccount
        {
            IdentityProvider = "test",
            ExternalUuidId = userGuid,
            Username = "Test",
            Email = "",
            CreatedAt = DateTimeOffset.UtcNow,
            LastLogin = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await account.SaveUser(testUser);

        return testUser.Id;
    }

    public async Task CleanUserSetlist(long musicianId)
    {
        var setLists = await MusicianDataAccess.Setlist.GetSetLists(musicianId);
        foreach (var setlist in setLists)
        {
            await MusicianDataAccess.Setlist.DeleteSetlist((long)setlist.Id!, musicianId);
        }

        var songs = await MusicianDataAccess.Song.GetSongs(musicianId, true);
        foreach (var song in songs)
        {
            await MusicianDataAccess.Song.DeleteSong(song.Id!.Value, musicianId);
        }
    }
}