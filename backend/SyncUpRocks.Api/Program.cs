using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Threading.RateLimiting;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using SyncUpRocks.Api.Caches;
using SyncUpRocks.Api.Controllers;
using SyncUpRocks.Api.Security;
using SyncUpRocks.Data.Access;
using SyncUpRocks.Data.Access.TypeHandlers;
using SyncUpRocks.Data.Importers.SetList.v1;
using SyncUpRocks.Types;

using System.Net;
using System.Net.Http;
using System.Text;

var assemblies = AppDomain.CurrentDomain.GetAssemblies()
    .Where(a => a.FullName != null && a.FullName.StartsWith("SyncUpRocks"))
    .ToArray();

DapperEntityMapper.RegisterHandlers(assemblies);

// Use KeyCloak mappings, not MS ones
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// For docker secrets - files in /run/secrets/some__key__property
builder.Configuration.AddKeyPerFile(directoryPath: "/run/secrets", optional: true);

builder.Services.AddSyncUpRocksDataAccess();

builder.Services.AddHybridCache();
builder.Services.AddMemoryCache();
// FUTURE: Replace this with ValKey
builder.Services.AddSingleton<ITicketStore, MemoryCacheTicketStore>();

builder.Services.AddSingleton<UserMappingCache>();
builder.Services.AddSingleton<SongInformationCache>();
builder.Services.AddSingleton<SetlistImporter>();

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();

builder.Services.AddHealthChecks();

//builder.Services.AddOpenApi();

SyncUpRocks.Api.Settings.StartupExtensions.Configure(builder);
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

builder.Services.AddRateLimiter(options =>
{
    // Define the "upload_limit" policy
    options.AddPolicy("upload_limit", httpContext =>
    {
        // Get the UserID from your Keycloak/JWT claims
        // Fallback to IP address for unauthenticated requests (safety first!)
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? httpContext.Connection.RemoteIpAddress?.ToString()
                     ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,             // Can burst up to 10 uploads instantly
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            TokensPerPeriod = 2,         // Regain 2 upload "credits" every minute
            QueueLimit = 0,              // Don't queue uploads; reject immediately if over limit
            AutoReplenishment = true
        });
    });

    // Custom 429 response
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            ok = false,
            error = "You're saving too fast! Take a breather and try again in a minute."
        }, token);
    };
});

builder.ConfigureAuthentication();

var app = builder.Build();

app.UseRateLimiter();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

app.UseExceptionHandler();

app.UseForwardedHeaders();

// Only now do you add Auth and Controllers
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        // Return 200 OK with the version
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            version = version,
            serverTime = DateTime.UtcNow
        });
    }
});

app.MapHealthChecks("/health-detail", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        // 1. Resolve your custom service from the request's DI container
        var myHealthChecks = context.RequestServices.GetRequiredService<IEnumerable<IHealthCheck>>();

        context.Response.ContentType = "application/json";

        var checkTasks = myHealthChecks.Select(check => check.GetReport(context.RequestAborted));
        var results = await Task.WhenAll(checkTasks);

        var urls = new[]
        {
            "http://bg-keycloak:8080/realms/bandguy/.well-known/openid-configuration"
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var sb = new StringBuilder();
        sb.AppendLine("\n--- Internal Network Diagnostics ---");

        foreach (var url in urls)
        {
            var uri = new Uri(url);
            sb.AppendLine($"\nTesting: {url}");

            // 1. DNS Resolution Check
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(uri.Host);
                var ipList = string.Join(", ", addresses.Select(a => a.ToString()));
                sb.AppendLine($"   Resolved {uri.Host} to: {ipList}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  DNS Lookup Failed for {uri.Host}: {ex.Message}");
                continue; // No point in trying the request if DNS failed
            }

            // 2. HTTP Connectivity Check
            try
            {
                // We use GetAsync with ResponseHeadersRead to avoid downloading large bodies
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                sb.AppendLine($"  Response: {(int)response.StatusCode} {response.StatusCode}");
            }
            catch (HttpRequestException ex)
            {
                sb.AppendLine($" HTTP Request Failed: {ex.Message}");
                if (ex.InnerException != null)
                    sb.AppendLine($"      Inner: {ex.InnerException.Message}");
            }
            catch (TaskCanceledException)
            {
                sb.AppendLine(" Request Timed Out (Possible Firewall/Closed Port)");
            }
        }

        sb.AppendLine("\n--- End of Diagnostics ---\n");

        await context.Response.WriteAsJsonAsync(new
        {
            status = results.All(r => r.SystemStatus == Health.Healthy) ? "Healthy" : "Unhealthy",
            version = version,
            serverTime = DateTime.UtcNow,
            environment = app.Environment.EnvironmentName,
            details = results,
            network = sb.ToString(),
        });
    }
});

app.Run();
