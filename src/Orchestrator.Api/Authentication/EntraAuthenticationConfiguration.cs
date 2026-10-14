using Fsp.Orchestrator.Api.Hosting;

namespace Fsp.Orchestrator.Api.Authentication;

public sealed record EntraAuthenticationConfiguration(
    string Instance,
    string TenantId,
    string ClientId,
    string SpaClientId,
    string ScopeName)
{
    public const string SectionName = "Authentication:Entra";

    public bool IsConfigured =>
        Uri.TryCreate(Instance, UriKind.Absolute, out _)
        && Guid.TryParse(TenantId, out _)
        && Guid.TryParse(ClientId, out _)
        && Guid.TryParse(SpaClientId, out _)
        && !string.IsNullOrWhiteSpace(ScopeName);

    public string ApiScope => IsConfigured
        ? $"api://{ClientId}/{ScopeName}"
        : string.Empty;

    public string Authority => $"{Instance.TrimEnd('/')}/{TenantId}";

    public string RedirectUri => $"{LoopbackHosting.Origin}/auth/callback";

    public static EntraAuthenticationConfiguration From(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new EntraAuthenticationConfiguration(
            section["Instance"]?.Trim() ?? "https://login.microsoftonline.com/",
            section["TenantId"]?.Trim() ?? string.Empty,
            section["ClientId"]?.Trim() ?? string.Empty,
            section["SpaClientId"]?.Trim() ?? string.Empty,
            section["ScopeName"]?.Trim() ?? "orchestrator.access_as_user");
    }
}
