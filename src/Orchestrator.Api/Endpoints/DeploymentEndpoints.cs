using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Application.Deployments;
using Fsp.Orchestrator.Application.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Fsp.Orchestrator.Api.Endpoints;

public static class DeploymentEndpoints
{
    public static RouteGroupBuilder MapDeployments(this RouteGroupBuilder endpoints)
    {
        endpoints.MapPost(
                "/deployments",
                RegisterAsync)
            .RequireAuthorization(OrchestratorRoles.Operator)
            .WithName("RegisterDeployment")
            .WithSummary("Register an existing FSP deployment")
            .WithDescription(
                "Probes TLS, health, readiness, identity, version, topology, "
                + "and required APIs before atomically persisting inventory.")
            .Produces<DeploymentRegistrationResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ProblemDetails>(StatusCodes.Status502BadGateway);

        endpoints.MapGet(
                "/deployments/{deploymentId:guid}",
                async Task<Results<Ok<DeploymentResponse>, NotFound>> (
                    Guid deploymentId,
                    IInventoryStore inventoryStore,
                    CancellationToken cancellationToken) =>
                {
                    var deployment = await inventoryStore.GetAsync(
                        deploymentId,
                        cancellationToken);
                    return deployment is null
                        ? TypedResults.NotFound()
                        : TypedResults.Ok(Map(deployment));
                })
            .RequireAuthorization(OrchestratorRoles.Reader)
            .WithName("GetDeployment")
            .WithSummary("Get a registered deployment")
            .WithDescription(
                "Returns non-secret inventory fields and credential references.")
            .Produces<DeploymentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterDeploymentRequest request,
        IDeploymentRegistrationService registrationService,
        IAuditMetadataStore auditStore,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DeploymentRegistrationRequest registration;
        try
        {
            registration = request.ToApplicationModel();
        }
        catch (UriFormatException)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Invalid deployment registration",
                "Endpoint and credential references must be absolute URIs.",
                "deployment.uri_invalid");
        }

        try
        {
            var result = await registrationService.RegisterAsync(
                registration,
                cancellationToken);
            await auditStore.AppendAsync(
                Audit(
                    user,
                    httpContext,
                    registration,
                    "succeeded"),
                cancellationToken);
            return TypedResults.Created(
                $"/api/deployments/{result.Deployment.Id}",
                new DeploymentRegistrationResponse(
                    Map(result.Deployment),
                    result.Components.Select(
                            component => new ObservedComponentResponse(
                                component.Name,
                                component.Version,
                                component.Status))
                        .ToArray()));
        }
        catch (DeploymentRegistrationException exception)
        {
            await auditStore.AppendAsync(
                Audit(
                    user,
                    httpContext,
                    registration,
                    exception.Code),
                CancellationToken.None);
            var status = exception.Failure switch
            {
                DeploymentRegistrationFailure.InvalidInput =>
                    StatusCodes.Status400BadRequest,
                DeploymentRegistrationFailure.DuplicateEndpoint =>
                    StatusCodes.Status409Conflict,
                DeploymentRegistrationFailure.Unreachable
                    or DeploymentRegistrationFailure.TlsValidationFailed =>
                    StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status422UnprocessableEntity,
            };
            return Problem(
                status,
                "Deployment registration failed",
                exception.Message,
                exception.Code);
        }
    }

    private static ProblemHttpResult Problem(
        int status,
        string title,
        string detail,
        string code) =>
        TypedResults.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
            });

    private static AppendAuditMetadata Audit(
        ClaimsPrincipal user,
        HttpContext httpContext,
        DeploymentRegistrationRequest request,
        string result)
    {
        var safeInput = string.Join(
            "\n",
            request.Name,
            request.ManagerEndpoint,
            request.AzureResourceId ?? string.Empty,
            request.CredentialKind,
            request.CredentialReference?.ToString() ?? string.Empty);
        return new AppendAuditMetadata(
            DateTimeOffset.UtcNow,
            user.FindFirstValue("oid") ?? string.Empty,
            user.FindFirstValue("tid") ?? string.Empty,
            "deployment.register",
            "fsp.deployment",
            request.Id.ToString(),
            result,
            httpContext.TraceIdentifier,
            Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(safeInput)))
                .ToLowerInvariant());
    }

    private static DeploymentResponse Map(DeploymentInventoryItem deployment) =>
        new(
            deployment.Id,
            deployment.Name,
            deployment.Topology,
            deployment.ManagerEndpoint,
            deployment.AzureResourceId,
            deployment.CredentialKind,
            deployment.CredentialReference,
            deployment.CreatedAt,
            deployment.UpdatedAt,
            deployment.Version);
}

/// <summary>Payload for registering an existing FSP deployment.</summary>
public sealed record RegisterDeploymentRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Required, MaxLength(2048)]
    public required string ManagerEndpoint { get; init; }

    [MaxLength(2048)]
    public string? AzureResourceId { get; init; }

    public FspCredentialKind CredentialKind { get; init; }

    [MaxLength(2048)]
    public string? CredentialReference { get; init; }

    internal DeploymentRegistrationRequest ToApplicationModel() =>
        new(
            Guid.NewGuid(),
            Name,
            new Uri(ManagerEndpoint, UriKind.Absolute),
            AzureResourceId,
            CredentialKind,
            string.IsNullOrWhiteSpace(CredentialReference)
                ? null
                : new Uri(CredentialReference, UriKind.Absolute));
}

/// <summary>Represents a successfully registered FSP deployment.</summary>
public sealed record DeploymentRegistrationResponse(
    DeploymentResponse Deployment,
    IReadOnlyList<ObservedComponentResponse> Components);

/// <summary>Represents non-secret deployment inventory.</summary>
public sealed record DeploymentResponse(
    Guid Id,
    string Name,
    string Topology,
    Uri ManagerEndpoint,
    string? AzureResourceId,
    string? CredentialKind,
    Uri? CredentialReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

/// <summary>Represents a component version observed during registration.</summary>
public sealed record ObservedComponentResponse(
    string Name,
    string Version,
    string Status);
