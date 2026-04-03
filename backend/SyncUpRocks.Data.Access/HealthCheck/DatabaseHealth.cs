using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SyncUpRocks.Types;

namespace SyncUpRocks.Data.Access.HealthCheck;

public class DatabaseHealth(
    ILogger<DatabaseHealth> _logger,
    IOptions<ConnectionStrings> _connectionStringOptions
    ) : IHealthCheck
{
    public async Task<HealthReport> GetReport(CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        bool hadError = false;

        try
        {
            sb.AppendLine("Connecting to musician db...");
            {
                using var connection = new NpgsqlConnection(_connectionStringOptions.Value.BandguyDatabase);
                await connection.OpenAsync(cancellationToken);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                await command.ExecuteScalarAsync(cancellationToken);
            }

            sb.AppendLine("Connecting to api db...");
            {
                using var connection = new NpgsqlConnection(_connectionStringOptions.Value.WebApiDatabase);
                await connection.OpenAsync(cancellationToken);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                await command.ExecuteScalarAsync(cancellationToken);
            }

            sb.AppendLine("DB tests OK");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sb.AppendLine("DB tests failed");
            _logger.LogError(ex, "Error Checking Database Health");
            hadError = true;
        }

        return new HealthReport("DatabaseHealth", hadError ? Health.Unhealthy : Health.Healthy, false, sb.ToString());
    }
}
