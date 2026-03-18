using Npgsql;
using Dapper;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddOpenApi();

//builder.Services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Valkey")!));

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

app.Run();
