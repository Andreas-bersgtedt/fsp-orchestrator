using Microsoft.AspNetCore.Authorization;

namespace Fsp.Orchestrator.Api.Authentication;

public sealed class TenantRequirementHandler
    : AuthorizationHandler<TenantRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantRequirement requirement)
    {
        if (context.User.HasClaim(
                "tid",
                requirement.TenantId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class ScopeRequirementHandler
    : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        var scopes = context.User.FindAll("scp")
            .SelectMany(claim => claim.Value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (scopes.Contains(requirement.Scope, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class AuthorizedClientRequirementHandler
    : AuthorizationHandler<AuthorizedClientRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AuthorizedClientRequirement requirement)
    {
        var authorizedParty = context.User.FindFirst("azp")?.Value
            ?? context.User.FindFirst("appid")?.Value;
        if (string.Equals(
                authorizedParty,
                requirement.ClientId,
                StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
