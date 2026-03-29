using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyncUpRocks.Data.Access;
using SyncUpRocks.Data.Access.Account;
using SyncUpRocks.Data.Access.Musician;
using SyncUpRocks.Data.Access.S3;
using SyncUpRocks.Data.Importers.SetList.v1;

namespace SyncUpRocks.unit.tests;

public class ImporterTests
{
    [Fact]
    public async Task Test1()
    {
        var credentials = new BasicAWSCredentials("remote-identity", "remote-credential");

        var config = new AmazonS3Config
        {
            ServiceURL = "http://127.0.0.1:9090",
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
            UseHttp = true
        };

        IAmazonS3 s3Client = new AmazonS3Client(credentials, config);

        var connection = new ConnectionStrings { BandguyDatabase = "Host=127.0.0.1;Database=bandguy;Username=myuser;Password=mypassword", SongBucket = "data" };
        var connectionMonitor = new ValueBasedOptionsMonitor<ConnectionStrings>(connection);
        var access = new MusicianDataAccess(connectionMonitor);
        var importer = new SetlistImporter(new NullLogger<SetlistImporter>(), access, new S3DataTransfer(new NullLogger<S3DataTransfer>(), s3Client, connectionMonitor));

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

        foreach (var setlist in setLists)
        {
            await access.Setlist.DeleteSetlist((long)setlist.Id!, Guid.Empty);
        }
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