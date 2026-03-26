using System.Reflection.Metadata.Ecma335;
using api.Controllers.User.Models.v1;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.User;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None, Duration = 0)]
public class LoginController : ControllerBase
{
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl)
    {
        return Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/jam" }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// For SPA/BFF, this API is used to check if currently logged in - if not, SPA can
    /// </summary>
    /// <returns></returns>
    [HttpGet("loggedin")]
    public ActionResult<ApiResponseBase<LoggedInStatus>> LoggedIn()
    {
        bool isLoggedIn = User.Identity?.IsAuthenticated ?? false;

        // MAYBE: Return SID? to clear out any session?
        var response = new ApiResponseBase<LoggedInStatus>(true, 
            new LoggedInStatus(
                isLoggedIn,
                User.FindFirst("name")?.Value ?? "",
                User.FindFirst("preferred_username")?.Value ?? "", 
                "/api/auth/login", 
                "/api/auth/logout"));

        if (isLoggedIn)
            return Ok(response);

        return Unauthorized(response);
    }

    /// <summary>
    /// Clears any cookie/session, and will return redirect
    /// </summary>
    /// <returns></returns>
    [HttpGet("logout")]
    public IActionResult Logout()
    {
        // 1. Clears the local .NET cookie (from your Postgres-backed Data Protection)
        // 2. Redirects the browser to Keycloak's 'end_session_endpoint'
        //await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        //await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);

        //return Redirect("/");
        return SignOut(new AuthenticationProperties { RedirectUri = "/" }, CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("debug")]
    public IActionResult DebugClaims()
    {

        return Ok(new
        {
            UserIdentity = new
            {
                User.Identity?.IsAuthenticated
            },
            Claims = User.Claims.Select(c => new { c.Type, c.Value })
        });
    }
}
