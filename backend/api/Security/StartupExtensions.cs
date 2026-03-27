using System.Net;
using api.Services;
using api.Settings;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace api.Security;

/// <summary>
/// Extension Methods for Startup Authentication Configuration
/// </summary>
public static class StartupExtensions
{
    public static WebApplicationBuilder ConfigureAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ForwardedHeadersOptions>().PostConfigure<IConfiguration>((options, config) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            // SECURITY: In local dev, we clear the known proxies so it trusts the Docker Gateway.
            // In production, you would add your specific Traefik IP here.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            var proxySettings = builder.Configuration.GetSection("ProxySettings").Get<ProxySettings>() ?? new();

            foreach (var networkStr in proxySettings.TrustedNetworks)
            {
                if (System.Net.IPNetwork.TryParse(networkStr, out var network))
                    options.KnownIPNetworks.Add(network);
            }

            // Add specific Proxy IPs
            foreach (var proxyIp in proxySettings.TrustedProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxyIp));
            }
        });

        // Program.cs
        builder.Services.AddAuthentication(options =>
        {
            // The default local session is managed by a Cookie
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;

            // When a user isn't logged in, send them to Keycloak (OIDC)
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = "web.api.user";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

            // Where to go if the user tries to hit [Authorize] without a cookie
            options.LoginPath = "/api/auth/login";
        });

        builder.Services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthenticationSettings>>((options, authSettings) =>
            {
                var conf = authSettings.Value.OpenIdConnectOptions;

                // Set these FIRST so the internal ConfigurationManager can initialize correctly
                options.Authority = conf.Authority;
                options.MetadataAddress = conf.MetadataAddress;
                options.ClientId = conf.ClientId;

                // --- Rest of your standard config ---
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                options.SaveTokens = true;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.CallbackPath = "/auth/signin-oidc";
                options.SignedOutCallbackPath = "/auth/signout-callback-oidc";

                // FUTURE: Verify these are locked down as much as possible
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.IsEssential = true;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.HttpOnly = true;
                options.NonceCookie.IsEssential = true;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "preferred_username",
                    RoleClaimType = "roles",
                    ValidateIssuer = true
                };

                options.Events = new OpenIdConnectEvents
                {
                    OnRemoteFailure = context =>
                    {
                        // FUTURE: Have a generic failure page...?
                        context.Response.Redirect("/error?message=" + context.Failure?.Message);
                        context.HandleResponse();
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var userService = context.HttpContext.RequestServices.GetRequiredService<IUserAccountService>();
                        var user = await userService.GetOrCreateUserAsync(context.Principal!, context.HttpContext.RequestAborted);
                        if (user.IsDisabled)
                        {
                            // FUTURE: Log Failure. Create account disabled URL
                            context.Fail("This account has been disabled.");
                            context.Response.Redirect("/account-disabled");
                            context.HandleResponse();
                        }
                    }
                };
            });

        builder.Services.AddAuthentication().AddOpenIdConnect();

        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .PostConfigure<ITicketStore>((options, ticketStore) =>
            {
                options.SessionStore = ticketStore;
            });

        // Create a Policy that accepts BOTH
        builder.Services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                //.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, "ForwardedScheme")
                .Build();
        });

        return builder;
    }
}
