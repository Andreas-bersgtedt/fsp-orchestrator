using System.Security.Claims;
using Fsp.Orchestrator.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Fsp.Orchestrator.Api.Endpoints;

public static class AuthenticationEndpoints
{
    public static RouteGroupBuilder MapAuthentication(
        this RouteGroupBuilder endpoints)
    {
        endpoints.MapGet(
                "/auth/config",
                Ok<AuthenticationConfigurationResponse> (
                    EntraAuthenticationConfiguration configuration,
                    CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return TypedResults.Ok(
                        new AuthenticationConfigurationResponse(
                            configuration.IsConfigured,
                            configuration.Authority,
                            configuration.TenantId,
                            configuration.SpaClientId,
                            configuration.ClientId,
                            configuration.ApiScope,
                            configuration.RedirectUri,
                            new BootstrapAuthenticationConfigurationResponse(
                                BootstrapAuthenticationConfiguration.SpaClientId,
                                BootstrapAuthenticationConfiguration.Authority,
                                BootstrapAuthenticationConfiguration.RedirectUri,
                                BootstrapAuthenticationConfiguration.DiscoveryScopes,
                                BootstrapAuthenticationConfiguration.CreationScopes)));
                })
            .AllowAnonymous()
            .WithName("GetAuthenticationConfiguration")
            .WithSummary("Get public authentication configuration")
            .WithDescription(
                "Returns public Entra identifiers used for PKCE sign-in. "
                + "No key, secret, certificate, or token is returned.")
            .Produces<AuthenticationConfigurationResponse>(StatusCodes.Status200OK);

        endpoints.MapGet(
                "/session",
                async Task<Ok<SessionResponse>> (
                    ClaimsPrincipal user,
                    IAuthorizationService authorization,
                    CancellationToken cancellationToken) =>
                {
                    var roles = user.FindAll("roles")
                        .Select(claim => claim.Value)
                        .Where(OrchestratorRoles.All.Contains)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    var permissions = new Dictionary<string, bool>(
                        StringComparer.Ordinal);
                    foreach (var role in OrchestratorRoles.All)
                    {
                        var result = await authorization.AuthorizeAsync(
                            user,
                            resource: null,
                            role);
                        permissions[role] = result.Succeeded;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    return TypedResults.Ok(
                        new SessionResponse(
                            user.FindFirstValue("oid") ?? string.Empty,
                            user.FindFirstValue("tid") ?? string.Empty,
                            user.FindFirstValue("name") ?? string.Empty,
                            user.FindFirstValue("preferred_username") ?? string.Empty,
                            roles,
                            permissions));
                })
            .WithName("GetOperatorSession")
            .WithSummary("Get the signed-in operator session")
            .WithDescription(
                "Returns safe identity, assigned roles, and evaluated API permissions. "
                + "No access token or raw claims are returned.")
            .Produces<SessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}

/// <summary>Public identifiers required for SPA authorization-code flow with PKCE.</summary>
public sealed record AuthenticationConfigurationResponse(
    bool Configured,
    string Authority,
    string TenantId,
    string SpaClientId,
    string ApiClientId,
    string ApiScope,
    string RedirectUri,
    BootstrapAuthenticationConfigurationResponse Bootstrap);

/// <summary>Public bootstrap SPA settings used before tenant registrations exist.</summary>
public sealed record BootstrapAuthenticationConfigurationResponse(
    string SpaClientId,
    string Authority,
    string RedirectUri,
    IReadOnlyList<string> DiscoveryScopes,
    IReadOnlyList<string> CreationScopes);

/// <summary>Safe information about the signed-in Orchestrator operator.</summary>
public sealed record SessionResponse(
    string ObjectId,
    string TenantId,
    string DisplayName,
    string Username,
    IReadOnlyList<string> Roles,
    IReadOnlyDictionary<string, bool> Permissions);
