using api.Controllers.User.Models.v1;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.User;

[ApiController]
[Route("api/user/auth")]
public class LoginController : ControllerBase
{
    /// <summary>
    /// For SPA/BFF, this API is used to check if currently logged in - if not, SPA can
    /// </summary>
    /// <returns></returns>
    [HttpGet("loggedin")]
    public ActionResult<ApiResponseBase<LoggedInStatus>> LoggedIn()
    {
        // TODO: If logged in (cookie), return current username/success
        // If logged out, return false/error
        var response = new ApiResponseBase<LoggedInStatus>(false, new LoggedInStatus(false, "", null, null));

        return Unauthorized(response);
    }

    /// <summary>
    /// Clears any cookie/session, and will return redirect
    /// </summary>
    /// <returns></returns>
    [HttpGet("logout")]
    public async Task<ActionResult> Logout()
    {
        // 1. Clears the local .NET cookie (from your Postgres-backed Data Protection)
        // 2. Redirects the browser to Keycloak's 'end_session_endpoint'
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);

        return Redirect("/");
    }
}
