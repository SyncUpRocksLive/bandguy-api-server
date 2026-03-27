using api.Controllers.User.Models.v1;
using api.Security;
using api.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace api.Controllers.User;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None, Duration = 0)]
public class LoginController(
    IOptions<AuthenticationSettings> _authenticationSettingsOption) : ControllerBase
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
        var principal = this.GetApiPrincipal();

        // MAYBE: Return SID? to clear out any session? secret?
        var response = new ApiResponseBase<LoggedInStatus>(true, 
            new LoggedInStatus(principal.IsAuthenticated, principal.UserId, principal.UserProfileName, principal.Username, "/api/auth/login", "/api/auth/logout"));

        if (principal.IsAuthenticated)
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
        return SignOut(new AuthenticationProperties { RedirectUri = "/" }, CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("debug")]
    public IActionResult DebugClaims(string? passPhrase)
    {
        if (_authenticationSettingsOption.Value.DebugPassPhrase != passPhrase)
            return Unauthorized();

        return Ok(new
        {
            Request.Scheme,
            Host = Request.Host.Value,
            RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers,
            UserIdentity = new
            {
                User.Identity?.IsAuthenticated,
                Claims = User.Claims.Select(c => new { c.Type, c.Value })
            },
        });
    }
}
