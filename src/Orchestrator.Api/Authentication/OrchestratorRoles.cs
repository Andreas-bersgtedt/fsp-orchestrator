namespace Fsp.Orchestrator.Api.Authentication;

public static class OrchestratorRoles
{
    public const string Reader = nameof(Reader);
    public const string Operator = nameof(Operator);
    public const string ConfigurationAdmin = nameof(ConfigurationAdmin);
    public const string DeploymentAdmin = nameof(DeploymentAdmin);
    public const string SecurityAdmin = nameof(SecurityAdmin);

    public static IReadOnlyList<string> All { get; } =
    [
        Reader,
        Operator,
        ConfigurationAdmin,
        DeploymentAdmin,
        SecurityAdmin,
    ];
}
