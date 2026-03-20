namespace api.Settings;

public static class SettingsConfiguration
{
    public static void Configure(IHostApplicationBuilder builder)
    {
        builder.Services.Configure<ConnectionStringSettings>(builder.Configuration.GetSection("ConnectionStrings"));
    }
}
