using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Api.Azure;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class AzureTokenSessionTests : IDisposable
{
    private readonly RSA signingKey = RSA.Create(2048);
    private readonly RsaSecurityKey securityKey;
    private readonly string tenantId = Guid.NewGuid().ToString();
    private readonly string objectId = Guid.NewGuid().ToString();
    private readonly string spaClientId = Guid.NewGuid().ToString();
    private readonly string issuer;

    public AzureTokenSessionTests()
    {
        securityKey = new RsaSecurityKey(signingKey)
        {
            KeyId = Guid.NewGuid().ToString(),
        };
        issuer = $"https://login.microsoftonline.com/{tenantId}/v2.0";
    }

    [Fact]
    public async Task MatchingArmAndOrchestratorIdentityIsAccepted()
    {
        var validator = Validator();

        var identity = await validator.ValidateAsync(
            Token(),
            OrchestratorPrincipal(),
            spaClientId,
            CancellationToken.None);

        Assert.Equal(tenantId, identity.TenantId);
        Assert.Equal(objectId, identity.ObjectId);
        Assert.True(identity.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task DifferentObjectIdIsRejected()
    {
        var validator = Validator();

        await Assert.ThrowsAsync<SecurityTokenValidationException>(
            () => validator.ValidateAsync(
                Token(objectIdOverride: Guid.NewGuid().ToString()),
                OrchestratorPrincipal(),
                spaClientId,
                CancellationToken.None));
    }

    [Fact]
    public void ExpiredInMemorySessionRequestsSignIn()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new InMemoryAzureTokenSessionStore(
            new FixedTimeProvider(now));
        store.Store(
            new ValidatedAzureIdentity(
                tenantId,
                objectId,
                now.AddSeconds(30)),
            "memory-only-token");

        Assert.Throws<AzureSignInRequiredException>(
            () => store.Retrieve(OrchestratorPrincipal()));
    }

    public void Dispose()
    {
        signingKey.Dispose();
        GC.SuppressFinalize(this);
    }

    private AzureDelegatedTokenValidator Validator()
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = issuer,
        };
        configuration.SigningKeys.Add(securityKey);
        return new AzureDelegatedTokenValidator(
            new TestConfigurationResolver(configuration));
    }

    private ClaimsPrincipal OrchestratorPrincipal() =>
        new(
            new ClaimsIdentity(
                [
                    new Claim("tid", tenantId),
                    new Claim("oid", objectId),
                ],
                "test"));

    private string Token(string? objectIdOverride = null)
    {
        var token = new JwtSecurityToken(
            issuer,
            "https://management.azure.com/",
            [
                new Claim("tid", tenantId),
                new Claim("oid", objectIdOverride ?? objectId),
                new Claim("azp", spaClientId),
                new Claim("scp", "user_impersonation"),
            ],
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class TestConfigurationResolver(
        OpenIdConnectConfiguration configuration)
        : IOpenIdConfigurationResolver
    {
        public Task<OpenIdConnectConfiguration> ResolveAsync(
            string tenantId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(configuration);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
