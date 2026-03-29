using System.Security.Claims;

namespace SyncUpRocks.Data.Access.Account;


public interface IUserAccountService
{
    public Task<UserAccount?> GetUserById(Guid uuid, CancellationToken cancellationToken);

    public Task SaveUser(UserAccount user, CancellationToken cancellationToken);

    public Task<UserAccount> GetOrCreateUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public class UserAccount
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastLogin { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsDisabled { get; set; }
    public string? DisabledReason { get; set; }
}