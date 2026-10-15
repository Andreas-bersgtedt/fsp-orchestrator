using Fsp.Orchestrator.Application.Identity;

namespace Fsp.Orchestrator.UnitTests;

public sealed class EntraRegistrationValidatorTests
{
    private readonly EntraRegistrationValidator validator = new();

    [Fact]
    public void ValidRegistrationPairPassesEveryCheck()
    {
        var result = validator.Validate(ValidPair());

        Assert.True(result.Passed);
        Assert.All(
            result.Checks,
            check => Assert.Equal(
                EntraRegistrationValidationStatus.Passed,
                check.Status));
    }

    [Fact]
    public void WrongTenantHasStableFailureCode()
    {
        var pair = ValidPair() with
        {
            SignedInTenantId = Guid.NewGuid().ToString(),
        };

        AssertFailed(pair, "entra.tenant.match");
    }

    [Fact]
    public void MissingRedirectUriHasStableFailureCode()
    {
        var pair = ValidPair();
        pair = pair with
        {
            Spa = pair.Spa with { SpaRedirectUris = [] },
        };

        AssertFailed(pair, "entra.spa.redirect_uri");
    }

    [Fact]
    public void MissingScopeHasStableFailureCode()
    {
        var pair = ValidPair();
        pair = pair with
        {
            Api = pair.Api with { DelegatedScopes = [] },
        };

        AssertFailed(pair, "entra.api.delegated_scope");
    }

    [Fact]
    public void RoleDriftHasStableFailureCode()
    {
        var pair = ValidPair();
        pair = pair with
        {
            Api = pair.Api with
            {
                AppRoles = pair.Api.AppRoles
                    .Where(role => role.Value != EntraRegistrationContract.SecurityAdmin)
                    .ToArray(),
            },
        };

        AssertFailed(pair, "entra.api.application_roles");
    }

    [Fact]
    public void UnauthorizedSpaHasStableFailureCode()
    {
        var pair = ValidPair();
        pair = pair with
        {
            Api = pair.Api with { PreAuthorizedApplications = [] },
        };

        AssertFailed(pair, "entra.api.authorized_spa");
    }

    [Fact]
    public void MissingServicePrincipalHasStableFailureCode()
    {
        var pair = ValidPair() with { ApiServicePrincipalExists = false };

        AssertFailed(pair, "entra.service_principals.present");
    }

    [Fact]
    public void MissingRoleAssignmentHasStableFailureCode()
    {
        var pair = ValidPair() with { SignedInUserRoles = [] };

        AssertFailed(pair, "entra.operator.role_assignment");
    }

    [Fact]
    public void ApplicationCredentialHasStableFailureCode()
    {
        var pair = ValidPair();
        pair = pair with
        {
            Spa = pair.Spa with { PasswordCredentialCount = 1 },
        };

        AssertFailed(pair, "entra.applications.no_credentials");
    }

    private void AssertFailed(EntraRegistrationPair pair, string code)
    {
        var result = validator.Validate(pair);

        Assert.False(result.Passed);
        var check = Assert.Single(
            result.Checks,
            candidate => candidate.Code == code);
        Assert.Equal(EntraRegistrationValidationStatus.Failed, check.Status);
        Assert.NotEmpty(check.Remediation);
    }

    private static EntraRegistrationPair ValidPair()
    {
        var tenantId = Guid.NewGuid().ToString();
        var spaAppId = Guid.NewGuid().ToString();
        var apiAppId = Guid.NewGuid().ToString();
        var scopeId = Guid.NewGuid();
        var emptyApplication = new EntraApplicationRegistration(
            Guid.NewGuid().ToString(),
            spaAppId,
            "FSP Orchestrator SPA",
            EntraRegistrationContract.SingleTenantAudience,
            [EntraRegistrationContract.RedirectUri],
            [],
            [],
            [],
            [],
            0,
            0);
        var api = new EntraApplicationRegistration(
            Guid.NewGuid().ToString(),
            apiAppId,
            "FSP Orchestrator API",
            EntraRegistrationContract.SingleTenantAudience,
            [],
            [$"api://{apiAppId}"],
            [
                new EntraDelegatedScope(
                    scopeId,
                    EntraRegistrationContract.ScopeName,
                    "User",
                    true),
            ],
            EntraRegistrationContract.Roles
                .Select(
                    role => new EntraAppRole(
                        Guid.NewGuid(),
                        role,
                        ["User"],
                        true))
                .ToArray(),
            [
                new EntraPreAuthorizedApplication(
                    spaAppId,
                    [scopeId]),
            ],
            0,
            0);

        return new EntraRegistrationPair(
            tenantId,
            tenantId,
            emptyApplication,
            api,
            true,
            true,
            [EntraRegistrationContract.SecurityAdmin]);
    }
}
