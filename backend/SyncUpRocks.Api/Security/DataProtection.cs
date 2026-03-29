using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;
using SyncUpRocks.Data.Access;

namespace SyncUpRocks.Api.Security;

public static class DataProtection
{
    public static void Configure(IHostApplicationBuilder builder)
    {
        // Setup & Configure .Net's Data Protection (cookie encryption, other protected resources)
        builder.Services.AddSingleton<IXmlRepository, PostgresXmlRepository>();
        builder.Services.AddDataProtection().SetApplicationName("syncup.rocks.webapi");
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
        {
            return new ConfigureOptions<KeyManagementOptions>(options => options.XmlRepository = sp.GetRequiredService<IXmlRepository>());
        });
    }
}

public class PostgresXmlRepository(
    IOptionsMonitor<ConnectionStrings> _connectionMonitor, 
    ILogger<PostgresXmlRepository> _logger) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        // FUTURE: Add retry, with backoff in case DB not yet ready. though, seems like .Net retries later...
        _logger.LogTrace("Loading Data Protection Keys");

        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.WebApiDatabase);

        // Explicitly pointing to our infrastructure schema
        const string sql = "SELECT xml_data FROM app.data_protection";
        var xmlStrings = conn.Query<string>(sql).Select(XElement.Parse).ToList().AsReadOnly();

        _logger.LogTrace("Loaded {count} DP Keys", xmlStrings.Count);
        return xmlStrings;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        // FUTURE: Add retry, with backoff in case DB gltches...

        _logger.LogInformation("Inserting new DP key {name}", friendlyName);

        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.WebApiDatabase);
        const string sql = @"
            INSERT INTO app.data_protection (friendly_name, xml_data) 
            VALUES (@name, @xml)";

        conn.Execute(sql, new
        {
            name = friendlyName,
            xml = element.ToString(SaveOptions.DisableFormatting)
        });
    }
}