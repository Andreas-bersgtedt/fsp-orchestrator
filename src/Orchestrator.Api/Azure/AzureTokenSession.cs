using System.Collections.Concurrent;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Application.Persistence;
using Microsoft.IdentityModel.Tokens;

namespace Fsp.Orchestrator.Api.Azure;

public interface IAzureDelegatedTokenValidator
{
    Task<ValidatedAzureIdentity> ValidateAsync(
        string accessToken,
        ClaimsPrincipal orchestratorUser,
        string authorizedClientId,
        CancellationToken cancellationToken);
}

public interface IAzureTokenSessionStore
{
    void Store(
        ValidatedAzureIdentity identity,
        string accessToken);

    AzureAccessTokenSession Retrieve(ClaimsPrincipal orchestratorUser);

    void Remove(ClaimsPrincipal orchestratorUser);
}

public interface IAzureJobTokenProvider
{
    Task<AzureAccessTokenSession> RequireAsync(
        JobInfo job,
        ClaimsPrincipal orchestratorUser,
        CancellationToken cancellationToken);
}

public sealed class AzureDelegatedTokenValidator(
    IOpenIdConfigurationResolver configurationResolver)
    : IAzureDelegatedTokenValidator
{
    private static readonly string[] ArmAudiences =
    [
        "https://management.azure.com",
        "https://management.azure.com/",
        "https://management.core.windows.net",
        "https://management.core.windows.net/",
    ];

    public async Task<ValidatedAzureIdentity> ValidateAsync(
        string accessToken,
        ClaimsPrincipal orchestratorUser,
        string authorizedClientId,
        CancellationToken cancellationToken)
    {
        var expectedTenantId = orchestratorUser.FindFirstValue("tid");
        var expectedObjectId = orchestratorUser.FindFirstValue("oid");
        if (!Guid.TryParse(expectedTenantId, out _)
            || !Guid.TryParse(expectedObjectId, out _)
            || !Guid.TryParse(authorizedClientId, out _))
        {
            throw new SecurityTokenValidationException(
                "The Orchestrator identity context is invalid.");
        }

        var openId = await configurationResolver.ResolveAsync(
            expectedTenantId,
            cancellationToken);
        var validation = new TokenValidationParameters
        {
            ClockSkew = TimeSpan.FromMinutes(1),
            IssuerSigningKeys = openId.SigningKeys,
            NameClaimType = "name",
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAudiences = ArmAudiences,
            ValidIssuers =
            [
                openId.Issuer,
                $"https://sts.windows.net/{expectedTenantId}/",
                $"https://login.microsoftonline.com/{expectedTenantId}/v2.0",
            ],
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
            out var validatedToken);
        RequireClaim(principal, "tid", expectedTenantId);
        RequireClaim(principal, "oid", expectedObjectId);

        var authorizedParty = principal.FindFirstValue("azp")
            ?? principal.FindFirstValue("appid");
        if (!string.Equals(
                authorizedParty,
                authorizedClientId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityTokenValidationException(
                "The ARM token was not issued to the authorized SPA.");
        }

        var scopes = principal.FindAll("scp")
            .SelectMany(
                claim => claim.Value.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries));
        if (!scopes.Contains("user_impersonation", StringComparer.Ordinal))
        {
            throw new SecurityTokenValidationException(
                "The ARM token is missing user_impersonation.");
        }

        return new ValidatedAzureIdentity(
            expectedTenantId,
            expectedObjectId,
            new DateTimeOffset(validatedToken.ValidTo, TimeSpan.Zero));
    }

    private static void RequireClaim(
        ClaimsPrincipal principal,
        string claimType,
        string expectedValue)
    {
        if (!principal.FindAll(claimType).Any(
                claim => string.Equals(
                    claim.Value,
                    expectedValue,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new SecurityTokenValidationException(
                $"The ARM token claim '{claimType}' does not match the Orchestrator token.");
        }
    }
}

public sealed class InMemoryAzureTokenSessionStore(TimeProvider timeProvider)
    : IAzureTokenSessionStore
{
    private readonly ConcurrentDictionary<string, AzureAccessTokenSession>
        sessions = new(StringComparer.Ordinal);

    public void Store(
        ValidatedAzureIdentity identity,
        string accessToken)
    {
        sessions[Key(identity.TenantId, identity.ObjectId)] =
            new AzureAccessTokenSession(
                identity.TenantId,
                identity.ObjectId,
                accessToken,
                identity.ExpiresAt);
    }

    public AzureAccessTokenSession Retrieve(ClaimsPrincipal orchestratorUser)
    {
        var (tenantId, objectId) = Identity(orchestratorUser);
        var key = Key(tenantId, objectId);
        if (!sessions.TryGetValue(key, out var session)
            || session.ExpiresAt <= timeProvider.GetUtcNow().AddMinutes(1))
        {
            sessions.TryRemove(key, out _);
            throw new AzureSignInRequiredException();
        }

        return session;
    }

    public void Remove(ClaimsPrincipal orchestratorUser)
    {
        var (tenantId, objectId) = Identity(orchestratorUser);
        sessions.TryRemove(Key(tenantId, objectId), out _);
    }

    private static (string TenantId, string ObjectId) Identity(
        ClaimsPrincipal principal)
    {
        var tenantId = principal.FindFirstValue("tid");
        var objectId = principal.FindFirstValue("oid");
        if (!Guid.TryParse(tenantId, out _)
            || !Guid.TryParse(objectId, out _))
        {
            throw new AzureSignInRequiredException();
        }

        return (tenantId, objectId);
    }

    private static string Key(string tenantId, string objectId) =>
        $"{tenantId.ToUpperInvariant()}:{objectId.ToUpperInvariant()}";
}

public sealed class AzureJobTokenProvider(
    IAzureTokenSessionStore sessions,
    IJobStore jobStore)
    : IAzureJobTokenProvider
{
    public async Task<AzureAccessTokenSession> RequireAsync(
        JobInfo job,
        ClaimsPrincipal orchestratorUser,
        CancellationToken cancellationToken)
    {
        try
        {
            return sessions.Retrieve(orchestratorUser);
        }
        catch (AzureSignInRequiredException)
        {
            if (job.Status == JobStatus.Running)
            {
                await jobStore.TransitionAsync(
                    new TransitionJob(
                        job.Id,
                        JobStatus.Paused,
                        job.Version,
                        "azure.sign_in_required"),
                    CancellationToken.None);
            }

            throw;
        }
    }
}

public sealed record ValidatedAzureIdentity(
    string TenantId,
    string ObjectId,
    DateTimeOffset ExpiresAt);

public sealed record AzureAccessTokenSession(
    string TenantId,
    string ObjectId,
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed class AzureSignInRequiredException()
    : Exception("Azure sign-in is required.");
