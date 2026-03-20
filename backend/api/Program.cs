using System.Reflection;
using api.Controllers;
using api.DataLayer;
using api.Security;
using api.Settings;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

DapperEntityMapper.RegisterHandlers([Assembly.GetExecutingAssembly()]);

var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddOpenApi();

SettingsConfiguration.Configure(builder);
DataProtection.Configure(builder);

// FUTURE: Use IOptionsMonitor here - in case connection path changes while app running
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Valkey")!));

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = string.Join(", ", context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));

            var response = new ApiResponseBase<object>(false, null, errors);
            return new BadRequestObjectResult(response);
        };
    });

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// --- 1. Configure the Middleware ---
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Tell .NET which headers to look for (Traefik sends all of these)
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

    // TODO SECURITY: In local dev, we clear the known proxies so it trusts the Docker Gateway.
    // In production, you would add your specific Traefik IP here.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
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
    options.Cookie.Name = "user";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

    // Where to go if the user tries to hit [Authorize] without a cookie
    options.LoginPath = "/api/auth/login";
})
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    // 1. Connection Details
    // Browser uses Authority; Server uses MetadataAddress (Docker Internal)
    options.Authority = "http://syncup.local:7080/realms/devrealm";
    options.MetadataAddress = "http://bg-keycloak:8080/realms/devrealm/.well-known/openid-configuration";

    options.ClientId = "myapp"; // Matches your Keycloak config
    options.RequireHttpsMetadata = builder.Environment.IsDevelopment() ? false : true; // Set to true in Production
    options.SaveTokens = true;
    options.ResponseType = OpenIdConnectResponseType.Code;

    // 2. Custom Handshake Paths (Your Identity Hub)
    // Must match what you put in Traefik and Keycloak Redirect URIs
    options.CallbackPath = "/auth/signin-oidc";
    options.SignedOutCallbackPath = "/auth/signout-callback-oidc";

    // 3. Data Mapping
    options.TokenValidationParameters = new TokenValidationParameters
    {
        // Maps Keycloak's 'preferred_username' (pj1) to User.Identity.Name
        NameClaimType = "preferred_username",
        // Maps Keycloak roles to User.IsInRole()
        RoleClaimType = "roles",
        ValidateIssuer = true
    };

    // 4. Events (Optional: Good for debugging)
    options.Events = new OpenIdConnectEvents
    {
        OnRemoteFailure = context =>
        {
            context.Response.Redirect("/error?message=" + context.Failure?.Message);
            context.HandleResponse();
            return Task.CompletedTask;
        }
    };
});

// Create a Policy that accepts BOTH
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, "ForwardedScheme")
        .Build();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

app.UseExceptionHandler();

// --- 2. Use the Middleware (Must be FIRST) ---
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/debug-headers", (HttpContext context) => new {
        Scheme = context.Request.Scheme,
        Host = context.Request.Host.Value,
        RemoteIp = context.Connection.RemoteIpAddress?.ToString(),
        Headers = context.Request.Headers
    });
}

// Only now do you add Auth and Controllers
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
