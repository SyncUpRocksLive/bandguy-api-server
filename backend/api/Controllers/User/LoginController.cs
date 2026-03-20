using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.User;

[ApiController]
public class LoginController : ControllerBase
{
    [HttpGet("logout")]
    public async Task Logout()
    {
        // 1. Clears the local .NET cookie (from your Postgres-backed Data Protection)
        // 2. Redirects the browser to Keycloak's 'end_session_endpoint'
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
    }
}
