using Microsoft.AspNetCore.Authorization;

namespace Fsp.Orchestrator.Api.Authentication;

public static class AuthorizationPolicies
{
    public const string Access = "Orchestrator.Access";
    public const string DatabaseDiagnostics = "Orchestrator.DatabaseDiagnostics";

    public static void Add(
        AuthorizationOptions options,
        EntraAuthenticationConfiguration configuration)
    {
        options.AddPolicy(
            Access,
            policy => AddAccessRequirements(policy, configuration));

        foreach (var role in OrchestratorRoles.All)
        {
            options.AddPolicy(
                role,
                policy =>
                {
                    AddAccessRequirements(policy, configuration);
                    policy.RequireRole(role);
                });
        }

        options.AddPolicy(
            DatabaseDiagnostics,
            policy =>
            {
                AddAccessRequirements(policy, configuration);
                policy.RequireRole(
                    OrchestratorRoles.Reader,
                    OrchestratorRoles.SecurityAdmin);
            });
    }

    private static void AddAccessRequirements(
        AuthorizationPolicyBuilder policy,
        EntraAuthenticationConfiguration configuration)
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(OrchestratorRoles.All);
        policy.AddRequirements(
            new TenantRequirement(configuration.TenantId),
            new ScopeRequirement(configuration.ScopeName),
            new AuthorizedClientRequirement(configuration.SpaClientId));
    }
}

public sealed record TenantRequirement(string TenantId) : IAuthorizationRequirement;

public sealed record ScopeRequirement(string Scope) : IAuthorizationRequirement;

public sealed record AuthorizedClientRequirement(string ClientId) : IAuthorizationRequirement;
