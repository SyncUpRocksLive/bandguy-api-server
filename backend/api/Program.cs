using System.Reflection;
using api.Controllers;
using api.DataLayer;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

DapperEntityMapper.RegisterHandlers([Assembly.GetExecutingAssembly()]);

var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddOpenApi();

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

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

app.UseExceptionHandler();

app.MapControllers();

app.Run();
