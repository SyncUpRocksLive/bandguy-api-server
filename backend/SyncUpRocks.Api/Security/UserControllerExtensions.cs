using System.Data.SqlTypes;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace SyncUpRocks.Api.Security;

public record ApiPrincipal(
    bool IsAuthenticated,
    Guid UserId,
    string UserProfileName,
    string Username,
    ClaimsPrincipal ClaimsUser
);

/// <summary>
/// Extension Methods for Controllers to leverage
/// </summary>
public static class UserControllerExtensions
{
    public static ApiPrincipal GetApiPrincipal(this ControllerBase controller)
    {
        if (controller.User.Identity == null)
            return new ApiPrincipal(false, Guid.Empty, "", "", controller.User);

        var sub = controller.User.FindFirst("sub")?.Value;

        return new ApiPrincipal(
            controller.User.Identity.IsAuthenticated,
            sub != null ? Guid.Parse(sub) : Guid.Empty,
            controller.User.FindFirst("name")?.Value ?? "",
            controller.User.FindFirst("preferred_username")?.Value ?? "",
            controller.User
        );
    }
}
