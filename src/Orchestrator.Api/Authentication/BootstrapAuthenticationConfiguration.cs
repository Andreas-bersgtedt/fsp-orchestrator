namespace Fsp.Orchestrator.Api.Authentication;

public static class BootstrapAuthenticationConfiguration
{
    public const string SpaClientId = "ca857dc1-f0bb-47e6-bc8a-02edc0e17cff";
    public const string Authority = "https://login.microsoftonline.com/organizations";
    public const string RedirectUri = "https://localhost:9443/auth/bootstrap-callback";

    public static IReadOnlyList<string> DiscoveryScopes { get; } =
    [
        "User.Read",
    ];

    public static IReadOnlyList<string> CreationScopes { get; } =
    [
        "Application.ReadWrite.All",
        "AppRoleAssignment.ReadWrite.All",
        "User.Read",
    ];
}
