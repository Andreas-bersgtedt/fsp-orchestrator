using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Fsp.Orchestrator.Api.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class CandidateApiTokenValidatorTests : IDisposable
{
    private readonly RSA signingKey = RSA.Create(2048);
    private readonly RsaSecurityKey securityKey;
    private readonly string tenantId = Guid.NewGuid().ToString();
    private readonly string spaClientId = Guid.NewGuid().ToString();
    private readonly string apiClientId = Guid.NewGuid().ToString();
    private readonly string objectId = Guid.NewGuid().ToString();
    private readonly string issuer;
    private bool disposed;

    public CandidateApiTokenValidatorTests()
    {
        securityKey = new RsaSecurityKey(signingKey)
        {
            KeyId = Guid.NewGuid().ToString(),
        };
        issuer = $"https://login.microsoftonline.com/{tenantId}/v2.0";
    }

    [Fact]
    public async Task ValidCandidateApiTokenReturnsSafeIdentity()
    {
        var validator = CreateValidator();

        var identity = await validator.ValidateAsync(
            CreateToken(),
            Candidate(),
            CancellationToken.None);

        Assert.Equal(objectId, identity.ObjectId);
        Assert.Equal(tenantId, identity.TenantId);
        Assert.Contains(OrchestratorRoles.SecurityAdmin, identity.Roles);
    }

    [Fact]
    public async Task MicrosoftGraphAudienceIsRejected()
    {
        var validator = CreateValidator();

        await Assert.ThrowsAsync<SecurityTokenInvalidAudienceException>(
            () => validator.ValidateAsync(
                CreateToken(audience: "00000003-0000-0000-c000-000000000000"),
                Candidate(),
                CancellationToken.None));
    }

    [Fact]
    public async Task WrongAuthorizedClientIsRejected()
    {
        var validator = CreateValidator();

        await Assert.ThrowsAsync<SecurityTokenValidationException>(
            () => validator.ValidateAsync(
                CreateToken(authorizedClient: Guid.NewGuid().ToString()),
                Candidate(),
                CancellationToken.None));
    }

    [Fact]
    public async Task MissingSecurityAdminRoleIsRejected()
    {
        var validator = CreateValidator();

        await Assert.ThrowsAsync<SecurityTokenValidationException>(
            () => validator.ValidateAsync(
                CreateToken(role: OrchestratorRoles.Reader),
                Candidate(),
                CancellationToken.None));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        signingKey.Dispose();
    }

    private CandidateApiTokenValidator CreateValidator()
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = issuer,
        };
        configuration.SigningKeys.Add(securityKey);
        return new CandidateApiTokenValidator(
            new TestConfigurationResolver(configuration));
    }

    private CandidateIdentityConfiguration Candidate() =>
        new(
            tenantId,
            spaClientId,
            apiClientId,
            "orchestrator.access_as_user");

    private string CreateToken(
        string? audience = null,
        string? authorizedClient = null,
        string role = OrchestratorRoles.SecurityAdmin)
    {
        var token = new JwtSecurityToken(
            issuer,
            audience ?? $"api://{apiClientId}",
            [
                new Claim("oid", objectId),
                new Claim("tid", tenantId),
                new Claim("azp", authorizedClient ?? spaClientId),
                new Claim("scp", "orchestrator.access_as_user"),
                new Claim("roles", role),
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
}
