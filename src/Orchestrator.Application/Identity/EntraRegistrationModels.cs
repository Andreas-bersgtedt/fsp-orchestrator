namespace Fsp.Orchestrator.Application.Identity;

public sealed record EntraRegistrationPair(
    string TenantId,
    string SignedInTenantId,
    EntraApplicationRegistration Spa,
    EntraApplicationRegistration Api,
    bool SpaServicePrincipalExists,
    bool ApiServicePrincipalExists,
    IReadOnlyList<string> SignedInUserRoles);

public sealed record EntraApplicationRegistration(
    string ObjectId,
    string AppId,
    string DisplayName,
    string SignInAudience,
    IReadOnlyList<string> SpaRedirectUris,
    IReadOnlyList<string> IdentifierUris,
    IReadOnlyList<EntraDelegatedScope> DelegatedScopes,
    IReadOnlyList<EntraAppRole> AppRoles,
    IReadOnlyList<EntraPreAuthorizedApplication> PreAuthorizedApplications,
    int PasswordCredentialCount,
    int KeyCredentialCount);

public sealed record EntraDelegatedScope(
    Guid Id,
    string Value,
    string Type,
    bool IsEnabled);

public sealed record EntraAppRole(
    Guid Id,
    string Value,
    IReadOnlyList<string> AllowedMemberTypes,
    bool IsEnabled);

public sealed record EntraPreAuthorizedApplication(
    string AppId,
    IReadOnlyList<Guid> DelegatedPermissionIds);

public sealed record EntraRegistrationValidationResult(
    bool Passed,
    IReadOnlyList<EntraRegistrationValidationCheck> Checks);

public sealed record EntraRegistrationValidationCheck(
    string Code,
    EntraRegistrationValidationStatus Status,
    string Message,
    string Remediation);

public enum EntraRegistrationValidationStatus
{
    Passed,
    Failed,
}
