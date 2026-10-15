namespace Fsp.Orchestrator.Application.Identity;

public static class EntraRegistrationContract
{
    public const string RedirectUri = "https://localhost:9443/auth/callback";
    public const string ScopeName = "orchestrator.access_as_user";
    public const string SingleTenantAudience = "AzureADMyOrg";
    public const string Reader = "Reader";
    public const string Operator = "Operator";
    public const string ConfigurationAdmin = "ConfigurationAdmin";
    public const string DeploymentAdmin = "DeploymentAdmin";
    public const string SecurityAdmin = "SecurityAdmin";

    public static IReadOnlyList<string> Roles { get; } =
    [
        Reader,
        Operator,
        ConfigurationAdmin,
        DeploymentAdmin,
        SecurityAdmin,
    ];
}
