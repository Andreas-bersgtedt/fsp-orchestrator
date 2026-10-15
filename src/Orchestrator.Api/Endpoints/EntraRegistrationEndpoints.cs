using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Application.Identity;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Fsp.Orchestrator.Api.Endpoints;

public static class EntraRegistrationEndpoints
{
    public static RouteGroupBuilder MapEntraRegistrations(
        this RouteGroupBuilder endpoints)
    {
        endpoints.MapPost(
                "/identity/registrations/validate",
                Ok<EntraRegistrationValidationResponse> (
                    ValidateEntraRegistrationsRequest request,
                    IEntraRegistrationValidator validator,
                    CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = validator.Validate(request.ToApplicationModel());
                    return TypedResults.Ok(
                        new EntraRegistrationValidationResponse(
                            result.Passed,
                            result.Checks
                                .Select(
                                    check => new EntraRegistrationValidationCheckResponse(
                                        check.Code,
                                        check.Status.ToString(),
                                        check.Message,
                                        check.Remediation))
                                .ToArray()));
                })
            .RequireAuthorization(OrchestratorRoles.SecurityAdmin)
            .WithName("ValidateEntraRegistrations")
            .WithSummary("Validate Entra application registrations")
            .WithDescription(
                "Validates normalized, non-secret SPA/API registration metadata "
                + "against the exact Orchestrator identity contract.")
            .Produces<EntraRegistrationValidationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}

/// <summary>Candidate SPA and API registration metadata to validate.</summary>
public sealed record ValidateEntraRegistrationsRequest
{
    public required string TenantId { get; init; }

    public required string SignedInTenantId { get; init; }

    public required EntraApplicationRegistrationRequest Spa { get; init; }

    public required EntraApplicationRegistrationRequest Api { get; init; }

    public required bool SpaServicePrincipalExists { get; init; }

    public required bool ApiServicePrincipalExists { get; init; }

    public required IReadOnlyList<string> SignedInUserRoles { get; init; }

    internal EntraRegistrationPair ToApplicationModel()
    {
        return new EntraRegistrationPair(
            TenantId,
            SignedInTenantId,
            Spa.ToApplicationModel(),
            Api.ToApplicationModel(),
            SpaServicePrincipalExists,
            ApiServicePrincipalExists,
            SignedInUserRoles);
    }
}

/// <summary>Non-secret metadata for one Entra application registration.</summary>
public sealed record EntraApplicationRegistrationRequest
{
    public required string ObjectId { get; init; }

    public required string AppId { get; init; }

    public required string DisplayName { get; init; }

    public required string SignInAudience { get; init; }

    public required IReadOnlyList<string> SpaRedirectUris { get; init; }

    public required IReadOnlyList<string> IdentifierUris { get; init; }

    public required IReadOnlyList<EntraDelegatedScopeRequest> DelegatedScopes { get; init; }

    public required IReadOnlyList<EntraAppRoleRequest> AppRoles { get; init; }

    public required IReadOnlyList<EntraPreAuthorizedApplicationRequest>
        PreAuthorizedApplications
    { get; init; }

    public required int PasswordCredentialCount { get; init; }

    public required int KeyCredentialCount { get; init; }

    internal EntraApplicationRegistration ToApplicationModel()
    {
        return new EntraApplicationRegistration(
            ObjectId,
            AppId,
            DisplayName,
            SignInAudience,
            SpaRedirectUris,
            IdentifierUris,
            DelegatedScopes
                .Select(scope => scope.ToApplicationModel())
                .ToArray(),
            AppRoles
                .Select(role => role.ToApplicationModel())
                .ToArray(),
            PreAuthorizedApplications
                .Select(application => application.ToApplicationModel())
                .ToArray(),
            PasswordCredentialCount,
            KeyCredentialCount);
    }
}

/// <summary>One delegated permission scope exposed by an API registration.</summary>
public sealed record EntraDelegatedScopeRequest(
    Guid Id,
    string Value,
    string Type,
    bool IsEnabled)
{
    internal EntraDelegatedScope ToApplicationModel() =>
        new(Id, Value, Type, IsEnabled);
}

/// <summary>One application role exposed by an API registration.</summary>
public sealed record EntraAppRoleRequest(
    Guid Id,
    string Value,
    IReadOnlyList<string> AllowedMemberTypes,
    bool IsEnabled)
{
    internal EntraAppRole ToApplicationModel() =>
        new(Id, Value, AllowedMemberTypes, IsEnabled);
}

/// <summary>A client application pre-authorized for delegated API permissions.</summary>
public sealed record EntraPreAuthorizedApplicationRequest(
    string AppId,
    IReadOnlyList<Guid> DelegatedPermissionIds)
{
    internal EntraPreAuthorizedApplication ToApplicationModel() =>
        new(AppId, DelegatedPermissionIds);
}

/// <summary>Complete deterministic validation result for a registration pair.</summary>
public sealed record EntraRegistrationValidationResponse(
    bool Passed,
    IReadOnlyList<EntraRegistrationValidationCheckResponse> Checks);

/// <summary>One stable registration validation result and safe remediation.</summary>
public sealed record EntraRegistrationValidationCheckResponse(
    string Code,
    string Status,
    string Message,
    string Remediation);
