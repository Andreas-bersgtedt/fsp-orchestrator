namespace Fsp.Orchestrator.Application.Identity;

public sealed class EntraRegistrationValidator : IEntraRegistrationValidator
{
    public EntraRegistrationValidationResult Validate(
        EntraRegistrationPair registration)
    {
        var checks = new List<EntraRegistrationValidationCheck>();

        Add(
            checks,
            "entra.tenant.match",
            Guid.TryParse(registration.TenantId, out _)
                && string.Equals(
                    registration.TenantId,
                    registration.SignedInTenantId,
                    StringComparison.OrdinalIgnoreCase),
            "The selected registrations belong to the signed-in tenant.",
            "Select registrations from the signed-in tenant.");
        Add(
            checks,
            "entra.client_ids.valid",
            Guid.TryParse(registration.Spa.ObjectId, out _)
                && Guid.TryParse(registration.Api.ObjectId, out _)
                && Guid.TryParse(registration.Spa.AppId, out _)
                && Guid.TryParse(registration.Api.AppId, out _)
                && !string.Equals(
                    registration.Spa.AppId,
                    registration.Api.AppId,
                    StringComparison.OrdinalIgnoreCase),
            "The SPA and API have distinct valid application IDs.",
            "Select distinct SPA and API application registrations.");
        Add(
            checks,
            "entra.audience.single_tenant",
            IsSingleTenant(registration.Spa) && IsSingleTenant(registration.Api),
            "Both application registrations are single-tenant.",
            "Set supported account types to this organizational directory only.");
        Add(
            checks,
            "entra.spa.redirect_uri",
            registration.Spa.SpaRedirectUris.Count == 1
                && registration.Spa.SpaRedirectUris.Contains(
                    EntraRegistrationContract.RedirectUri,
                    StringComparer.Ordinal),
            "The SPA has the exact Orchestrator callback URI.",
            $"Configure only {EntraRegistrationContract.RedirectUri} as the SPA redirect URI.");
        Add(
            checks,
            "entra.applications.no_credentials",
            HasNoCredentials(registration.Spa) && HasNoCredentials(registration.Api),
            "Neither application registration has a secret or certificate credential.",
            "Remove client secrets and certificate credentials; Orchestrator uses delegated PKCE.");

        var expectedIdentifierUri = $"api://{registration.Api.AppId}";
        Add(
            checks,
            "entra.api.identifier_uri",
            registration.Api.IdentifierUris.Count == 1
                && registration.Api.IdentifierUris.Contains(
                    expectedIdentifierUri,
                    StringComparer.OrdinalIgnoreCase),
            "The API application ID URI matches its client ID.",
            $"Set the API application ID URI to {expectedIdentifierUri}.");

        var scope = registration.Api.DelegatedScopes.SingleOrDefault(
            candidate => string.Equals(
                candidate.Value,
                EntraRegistrationContract.ScopeName,
                StringComparison.Ordinal));
        Add(
            checks,
            "entra.api.delegated_scope",
            registration.Api.DelegatedScopes.Count == 1
                && scope is { IsEnabled: true }
                && string.Equals(scope.Type, "User", StringComparison.Ordinal),
            "The delegated Orchestrator API scope is enabled for users.",
            $"Expose the delegated scope {EntraRegistrationContract.ScopeName} for users.");

        var rolesValid = EntraRegistrationContract.Roles.All(
            expectedRole => registration.Api.AppRoles.Any(
                role => role.IsEnabled
                    && role.Id != Guid.Empty
                    && string.Equals(role.Value, expectedRole, StringComparison.Ordinal)
                    && role.AllowedMemberTypes.Contains(
                        "User",
                        StringComparer.Ordinal)));
        Add(
            checks,
            "entra.api.application_roles",
            rolesValid
                && registration.Api.AppRoles.Count == EntraRegistrationContract.Roles.Count
                && registration.Api.AppRoles
                    .Where(role => EntraRegistrationContract.Roles.Contains(role.Value))
                    .Select(role => role.Id)
                    .Distinct()
                    .Count() == EntraRegistrationContract.Roles.Count,
            "The API application roles match Orchestrator policies.",
            "Define enabled user roles for Reader, Operator, ConfigurationAdmin, "
                + "DeploymentAdmin, and SecurityAdmin with unique IDs.");

        var spaPreAuthorized = scope is not null
            && registration.Api.PreAuthorizedApplications.Any(
                application => string.Equals(
                        application.AppId,
                        registration.Spa.AppId,
                        StringComparison.OrdinalIgnoreCase)
                    && application.DelegatedPermissionIds.Contains(scope.Id));
        Add(
            checks,
            "entra.api.authorized_spa",
            spaPreAuthorized,
            "The selected SPA is pre-authorized for the delegated API scope.",
            "Pre-authorize the selected SPA client for orchestrator.access_as_user.");
        Add(
            checks,
            "entra.service_principals.present",
            registration.SpaServicePrincipalExists
                && registration.ApiServicePrincipalExists,
            "Enterprise applications exist for both registrations.",
            "Create or consent the SPA and API service principals in this tenant.");
        Add(
            checks,
            "entra.operator.role_assignment",
            registration.SignedInUserRoles.Any(
                EntraRegistrationContract.Roles.Contains),
            "The signed-in operator has an Orchestrator application role.",
            "Assign at least one Orchestrator API role to the signed-in user.");

        return new EntraRegistrationValidationResult(
            checks.All(check => check.Status == EntraRegistrationValidationStatus.Passed),
            checks);
    }

    private static bool IsSingleTenant(EntraApplicationRegistration application)
    {
        return string.Equals(
            application.SignInAudience,
            EntraRegistrationContract.SingleTenantAudience,
            StringComparison.Ordinal);
    }

    private static bool HasNoCredentials(EntraApplicationRegistration application)
    {
        return application.PasswordCredentialCount == 0
            && application.KeyCredentialCount == 0;
    }

    private static void Add(
        List<EntraRegistrationValidationCheck> checks,
        string code,
        bool passed,
        string success,
        string remediation)
    {
        checks.Add(
            new EntraRegistrationValidationCheck(
                code,
                passed
                    ? EntraRegistrationValidationStatus.Passed
                    : EntraRegistrationValidationStatus.Failed,
                passed ? success : remediation,
                passed ? string.Empty : remediation));
    }
}
