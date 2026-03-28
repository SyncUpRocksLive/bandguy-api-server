using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using api.Controllers;
using api.DataLayer;
using api.Security;
using api.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

DapperEntityMapper.RegisterHandlers([Assembly.GetExecutingAssembly()]);

// Use KeyCloak mappings, not MS ones
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHybridCache();
builder.Services.AddMemoryCache();
// FUTURE: Replace this with ValKey
builder.Services.AddSingleton<ITicketStore, MemoryCacheTicketStore>();
builder.Services.AddSingleton<IUserAccountService, UserAccountService>();
builder.Services.AddSingleton<UserMappingCache>();

//builder.Services.AddOpenApi();

api.Settings.StartupExtensions.Configure(builder);
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

builder.ConfigureAuthentication();

var app = builder.Build();

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

app.Run();
