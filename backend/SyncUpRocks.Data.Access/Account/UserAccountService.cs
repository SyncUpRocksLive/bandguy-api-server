using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace SyncUpRocks.Data.Access.Account;

public class UserAccountService(
    IOptionsMonitor<ConnectionStrings> _connectionMonitor,
    ILogger<UserAccountService> _logger) : IUserAccountService
{
    public async Task<UserAccount?> GetUserByExternalUuid(Guid externalUuid, CancellationToken cancellationToken = default)
    {
        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);

        return await conn.QueryFirstOrDefaultAsync<UserAccount?>(new CommandDefinition(
            @"SELECT 
                id AS Id, 
                identity_provider AS IdentityProvider,
                external_uuid AS ExternalUuid,
                username AS Username,
                email AS Email, 
                created_at AS CreatedAt,
                updated_at AS UpdatedAt,
                last_login AS LastLogin,
                is_disabled AS IsDisabled,
                disabled_reason AS DisabledReason
            FROM app.musicians 
            WHERE external_uuid = @ExternalUuid::uuid",
            new { ExternalUuid = externalUuid },
            cancellationToken: cancellationToken
        ));
    }

    public async Task SaveUser(UserAccount user, CancellationToken cancellationToken = default)
    {
        using var conn = new NpgsqlConnection(_connectionMonitor.CurrentValue.BandguyDatabase);

        const string sql = @"
        INSERT INTO app.musicians (identity_provider, external_uuid, username, email, created_at, updated_at, last_login, is_disabled, disabled_reason) 
        VALUES (@IdentityProvider, @ExternalUuidId, @Username, @Email, @CreatedAt, @UpdatedAt, @LastLogin, @IsDisabled, @DisabledReason)
        ON CONFLICT (id) DO UPDATE SET
            username = EXCLUDED.username,
            email = EXCLUDED.email,
            updated_at = NOW(),
            last_login = EXCLUDED.last_login,
            is_disabled = EXCLUDED.is_disabled,
            disabled_reason = EXCLUDED.disabled_reason
        RETURNING id;";

        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, user, cancellationToken: cancellationToken));
        if (user.Id == 0)
            user.Id = id;
    }

    public async Task<UserAccount> GetOrCreateUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var subId = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(subId))
            throw new Exception("Missing sub token");

        if (!Guid.TryParse(subId, out var externalUuid))
            throw new Exception("Invalid sub token");

        // 1. Check your DB (Postgres/Local List)
        var user = await GetUserByExternalUuid(externalUuid, cancellationToken);
        if (user == null)
        {
            user = new UserAccount
            {
                IdentityProvider = "keycloak",
                ExternalUuidId = externalUuid,
                Username = principal.FindFirst("preferred_username")!.Value!,
                Email = principal.FindFirst("email")!.Value!,
                CreatedAt = DateTimeOffset.UtcNow,
                LastLogin = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _logger.LogInformation("Creating User={uuid} name={name}", user.Id, user.Username);
            await SaveUser(user, cancellationToken);
        }

        // FUTURE: Based on LastLogin, determine if we should refresh the data in postgres (may have changed from identity provider)

        return user;
    }
}
