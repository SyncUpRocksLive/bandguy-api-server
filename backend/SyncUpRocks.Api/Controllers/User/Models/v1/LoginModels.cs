namespace SyncUpRocks.Api.Controllers.User.Models.v1;

/// <summary>
/// Login Status
/// </summary>
/// <param name="IsLoggedIn">True if logged in</param>
/// <param name="UserProfileName">If logged in, current username</param>
/// <param name="LogInUrl">If not logged in, redirection URL to attempt login</param>
/// <param name="LogOutUrl">If logged in, redirection URL to logout</param>
public record LoggedInStatus(
    bool IsLoggedIn,
    Guid UserId,
    string UserProfileName,
    string Username,
    string? LogInUrl,
    string? LogOutUrl
);
