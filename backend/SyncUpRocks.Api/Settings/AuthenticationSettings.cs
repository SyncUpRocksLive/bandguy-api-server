using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace SyncUpRocks.Api.Settings;

public class AuthenticationSettings
{
    public string? DebugPassPhrase { get; set; }

    public ProxySettings ProxySettings { get; set; } = new();

    public OpenIdConnectOptions OpenIdConnectOptions { get; set; } = new();
}

public class ProxySettings
{
    public List<string> TrustedNetworks { get; set; } = [];

    public List<string> TrustedProxies { get; set; } = [];
}