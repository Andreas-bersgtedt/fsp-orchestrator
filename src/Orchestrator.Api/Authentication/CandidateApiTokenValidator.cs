using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Fsp.Orchestrator.Api.Authentication;

public interface ICandidateApiTokenValidator
{
    Task<CandidateOperatorIdentity> ValidateAsync(
        string accessToken,
        CandidateIdentityConfiguration candidate,
        CancellationToken cancellationToken);
}

public interface IOpenIdConfigurationResolver
{
    Task<OpenIdConnectConfiguration> ResolveAsync(
        string tenantId,
        CancellationToken cancellationToken);
}

public sealed class CandidateApiTokenValidator(
    IOpenIdConfigurationResolver configurationResolver)
    : ICandidateApiTokenValidator
{
    public async Task<CandidateOperatorIdentity> ValidateAsync(
        string accessToken,
        CandidateIdentityConfiguration candidate,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(candidate.TenantId, out _)
            || !Guid.TryParse(candidate.SpaClientId, out _)
            || !Guid.TryParse(candidate.ApiClientId, out _)
            || string.IsNullOrWhiteSpace(candidate.ScopeName))
        {
            throw new SecurityTokenValidationException(
                "Candidate identity configuration contains invalid public identifiers.");
        }

        var openId = await configurationResolver.ResolveAsync(
            candidate.TenantId,
            cancellationToken);
        var validation = new TokenValidationParameters
        {
            ClockSkew = TimeSpan.FromMinutes(1),
            IssuerSigningKeys = openId.SigningKeys,
            NameClaimType = "name",
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            RoleClaimType = "roles",
            ValidAudiences =
            [
                candidate.ApiClientId,
                $"api://{candidate.ApiClientId}",
            ],
            ValidIssuer = openId.Issuer,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
        };
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false,
        };
        var principal = handler.ValidateToken(
            accessToken,
            validation,
            out _);

        RequireClaim(principal, "tid", candidate.TenantId);
        var authorizedParty = principal.FindFirstValue("azp")
            ?? principal.FindFirstValue("appid");
        if (!string.Equals(
                authorizedParty,
                candidate.SpaClientId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityTokenValidationException(
                "Candidate token was not issued to the selected SPA client.");
        }

        var scopes = principal.FindAll("scp")
            .SelectMany(claim => claim.Value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (!scopes.Contains(candidate.ScopeName, StringComparer.Ordinal))
        {
            throw new SecurityTokenValidationException(
                "Candidate token is missing the Orchestrator delegated scope.");
        }

        var objectId = principal.FindFirstValue("oid");
        if (!Guid.TryParse(objectId, out _))
        {
            throw new SecurityTokenValidationException(
                "Candidate token is missing a valid operator object ID.");
        }

        var roles = principal.FindAll("roles")
            .Select(claim => claim.Value)
            .Where(OrchestratorRoles.All.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!roles.Contains(OrchestratorRoles.SecurityAdmin, StringComparer.Ordinal))
        {
            throw new SecurityTokenValidationException(
                "Candidate operator must have the SecurityAdmin role.");
        }

        return new CandidateOperatorIdentity(objectId, candidate.TenantId, roles);
    }

    private static void RequireClaim(
        ClaimsPrincipal principal,
        string claimType,
        string expectedValue)
    {
        if (!principal.FindAll(claimType)
                .Any(
                    claim => string.Equals(
                        claim.Value,
                        expectedValue,
                        StringComparison.OrdinalIgnoreCase)))
        {
            throw new SecurityTokenValidationException(
                $"Candidate token claim '{claimType}' is invalid.");
        }
    }
}

public sealed class OpenIdConfigurationResolver
    : IOpenIdConfigurationResolver
{
    private readonly Dictionary<string, ConfigurationManager<OpenIdConnectConfiguration>>
        managers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock sync = new();

    public Task<OpenIdConnectConfiguration> ResolveAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(tenantId, out _))
        {
            throw new SecurityTokenValidationException(
                "Candidate tenant ID must be a GUID.");
        }

        ConfigurationManager<OpenIdConnectConfiguration> manager;
        lock (sync)
        {
            if (!managers.TryGetValue(tenantId, out manager!))
            {
                var metadata =
                    $"https://login.microsoftonline.com/{tenantId}/v2.0/"
                    + ".well-known/openid-configuration";
                manager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    metadata,
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever { RequireHttps = true });
                managers.Add(tenantId, manager);
            }
        }

        return manager.GetConfigurationAsync(cancellationToken);
    }
}

public sealed record CandidateIdentityConfiguration(
    string TenantId,
    string SpaClientId,
    string ApiClientId,
    string ScopeName);

public sealed record CandidateOperatorIdentity(
    string ObjectId,
    string TenantId,
    IReadOnlyList<string> Roles);
