using Fsp.Orchestrator.Application.Identity;

namespace Fsp.Orchestrator.Api.Authentication;

public static class OrchestratorRoles
{
    public const string Reader = EntraRegistrationContract.Reader;
    public const string Operator = EntraRegistrationContract.Operator;
    public const string ConfigurationAdmin = EntraRegistrationContract.ConfigurationAdmin;
    public const string DeploymentAdmin = EntraRegistrationContract.DeploymentAdmin;
    public const string SecurityAdmin = EntraRegistrationContract.SecurityAdmin;

    public static IReadOnlyList<string> All => EntraRegistrationContract.Roles;
}
