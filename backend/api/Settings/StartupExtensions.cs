namespace api.Settings;

public static class StartupExtensions
{
    public static void Configure(IHostApplicationBuilder builder)
    {
        builder.Services.Configure<ConnectionStringSettings>(builder.Configuration.GetSection("ConnectionStrings"));

        // FUTURE: When not IsDevelopment(), ensure that passPhrase and settings here are not null/empty
        builder.Services.Configure<AuthenticationSettings>(builder.Configuration.GetSection("Authentication"));
    }
}
