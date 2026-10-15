using System.Net.Http.Headers;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Api.Hosting;
using Fsp.Orchestrator.Application.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.Tokens;

namespace Fsp.Orchestrator.Api.Endpoints;

public static class BootstrapEndpoints
{
    public static IEndpointRouteBuilder MapBootstrap(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/bootstrap/identity/commit",
                async Task<Results<Ok<BootstrapCommitResponse>, ProblemHttpResult>> (
                    BootstrapCommitRequest request,
                    HttpRequest httpRequest,
                    EntraAuthenticationConfiguration currentConfiguration,
                    ICandidateApiTokenValidator tokenValidator,
                    IEntraRegistrationValidator registrationValidator,
                    IIdentityConfigurationStore configurationStore,
                    IApplicationRestartScheduler restartScheduler,
                    CancellationToken cancellationToken) =>
                {
                    if (currentConfiguration.IsConfigured)
                    {
                        return TypedResults.Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            title: "Identity is already configured",
                            detail: "Bootstrap configuration cannot replace active identity settings.");
                    }

                    if (!TryGetBearerToken(httpRequest, out var accessToken))
                    {
                        return TypedResults.Problem(
                            statusCode: StatusCodes.Status401Unauthorized,
                            title: "Candidate API token required",
                            detail: "Provide a token minted for the candidate Orchestrator API.");
                    }

                    CandidateOperatorIdentity operatorIdentity;
                    try
                    {
                        operatorIdentity = await tokenValidator.ValidateAsync(
                            accessToken,
                            request.Candidate.ToAuthenticationModel(),
                            cancellationToken);
                    }
                    catch (SecurityTokenException)
                    {
                        return TypedResults.Problem(
                            statusCode: StatusCodes.Status401Unauthorized,
                            title: "Candidate token validation failed",
                            detail: "The candidate token did not satisfy the Orchestrator identity contract.");
                    }

                    if (!request.Candidate.Matches(request.Registration))
                    {
                        return TypedResults.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Candidate identifiers do not match",
                            detail: "Submitted public IDs must match the selected registration snapshots.");
                    }

                    var registration = request.Registration.ToApplicationModel() with
                    {
                        SignedInTenantId = operatorIdentity.TenantId,
                        SignedInUserRoles = operatorIdentity.Roles,
                    };
                    var validation = registrationValidator.Validate(registration);
                    if (!validation.Passed)
                    {
                        return TypedResults.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Registration validation failed",
                            detail: "Resolve every failed registration check before committing.");
                    }

                    await configurationStore.CommitAsync(
                        new PersistedIdentityConfiguration(
                            "https://login.microsoftonline.com/",
                            request.Candidate.TenantId,
                            request.Candidate.ApiClientId,
                            request.Candidate.SpaClientId,
                            request.Candidate.ScopeName,
                            operatorIdentity.ObjectId,
                            DateTimeOffset.UtcNow),
                        cancellationToken);
                    try
                    {
                        restartScheduler.ScheduleRestart();
                    }
                    catch (InvalidOperationException)
                    {
                        await configurationStore.RollbackAsync(cancellationToken);
                        return TypedResults.Problem(
                            statusCode: StatusCodes.Status500InternalServerError,
                            title: "Identity restart scheduling failed",
                            detail: "The previous unconfigured identity state remains active.");
                    }

                    return TypedResults.Ok(
                        new BootstrapCommitResponse(
                            true,
                            true,
                            "Identity configuration committed. FSP Orchestrator is restarting "
                                + "to activate normal Entra authentication."));
                })
            .AllowAnonymous()
            .WithName("CommitBootstrapIdentity")
            .WithSummary("Validate and commit bootstrap identity configuration")
            .WithDescription(
                "Accepts only a candidate API token and public registration metadata. "
                + "Microsoft Graph tokens and anonymous writes are rejected.")
            .Produces<BootstrapCommitResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static bool TryGetBearerToken(
        HttpRequest request,
        out string token)
    {
        token = string.Empty;
        if (!AuthenticationHeaderValue.TryParse(
                request.Headers.Authorization,
                out var authorization)
            || !string.Equals(
                authorization.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            return false;
        }

        token = authorization.Parameter;
        return true;
    }
}

/// <summary>Candidate public identifiers and normalized registrations to commit.</summary>
public sealed record BootstrapCommitRequest
{
    public required CandidateIdentityConfigurationRequest Candidate { get; init; }

    public required ValidateEntraRegistrationsRequest Registration { get; init; }
}

/// <summary>Public identifiers for the candidate tenant SPA and API.</summary>
public sealed record CandidateIdentityConfigurationRequest
{
    public required string TenantId { get; init; }

    public required string SpaClientId { get; init; }

    public required string ApiClientId { get; init; }

    public required string ScopeName { get; init; }

    internal CandidateIdentityConfiguration ToAuthenticationModel() =>
        new(TenantId, SpaClientId, ApiClientId, ScopeName);

    internal bool Matches(ValidateEntraRegistrationsRequest registration) =>
        string.Equals(TenantId, registration.TenantId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            SpaClientId,
            registration.Spa.AppId,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            ApiClientId,
            registration.Api.AppId,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            ScopeName,
            EntraRegistrationContract.ScopeName,
            StringComparison.Ordinal);
}

/// <summary>Result of an atomic public identity configuration commit.</summary>
public sealed record BootstrapCommitResponse(
    bool Committed,
    bool RestartRequired,
    string Message);
