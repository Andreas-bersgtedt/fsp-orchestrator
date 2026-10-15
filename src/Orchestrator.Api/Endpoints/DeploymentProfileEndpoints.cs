using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Api.Azure;
using Fsp.Orchestrator.Application.Deployments;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Fsp.Orchestrator.Api.Endpoints;

public static class DeploymentProfileEndpoints
{
    public static RouteGroupBuilder MapDeploymentProfiles(
        this RouteGroupBuilder endpoints)
    {
        endpoints.MapGet(
                "/deployment-profiles",
                async Task<Ok<IReadOnlyList<DeploymentProfileDocument>>> (
                    IDeploymentProfileStore store,
                    CancellationToken cancellationToken) =>
                    TypedResults.Ok(
                        await store.ListAsync(cancellationToken)))
            .RequireAuthorization(OrchestratorRoles.DeploymentAdmin)
            .WithName("ListDeploymentProfiles")
            .WithSummary("List recent deployment profiles")
            .WithDescription(
                "Returns typed profile metadata without any Azure token.")
            .Produces<IReadOnlyList<DeploymentProfileDocument>>(
                StatusCodes.Status200OK);

        endpoints.MapPut(
                "/deployment-profiles/{profileId:guid}",
                SaveAsync)
            .RequireAuthorization(OrchestratorRoles.DeploymentAdmin)
            .WithName("SaveDeploymentProfile")
            .WithSummary("Create or update a typed Azure deployment profile")
            .WithDescription(
                "Validates and persists typed deployment input. A material "
                + "change invalidates the current prerequisite run, preview, and approval.")
            .Produces<DeploymentProfileStateResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        endpoints.MapGet(
                "/deployment-profiles/{profileId:guid}",
                GetAsync)
            .RequireAuthorization(OrchestratorRoles.DeploymentAdmin)
            .WithName("GetDeploymentProfile")
            .WithSummary("Get a deployment profile and current preview state")
            .WithDescription(
                "Returns the typed profile, structured what-if, and exact approval.")
            .Produces<DeploymentProfileStateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPost(
                "/deployment-profiles/{profileId:guid}/preview",
                PreviewAsync)
            .RequireAuthorization(OrchestratorRoles.DeploymentAdmin)
            .WithName("PreviewDeploymentProfile")
            .WithSummary("Run ARM validation and what-if")
            .WithDescription(
                "Requires a passing prerequisite run, then uses the delegated "
                + "operator ARM token for validation and structured what-if.")
            .Produces<DeploymentPreview>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status502BadGateway);

        endpoints.MapPost(
                "/deployment-profiles/{profileId:guid}/approval",
                ApproveAsync)
            .RequireAuthorization(OrchestratorRoles.DeploymentAdmin)
            .WithName("ApproveDeploymentProfilePreview")
            .WithSummary("Approve the exact current what-if")
            .WithDescription(
                "Records the signed-in actor and create, modify, and delete counts.")
            .Produces<DeploymentApproval>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> SaveAsync(
        Guid profileId,
        SaveDeploymentProfileRequest request,
        IDeploymentProfileService service,
        IDeploymentProfileStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.SaveAsync(
                request.ToProfile(profileId),
                cancellationToken);
            return TypedResults.Ok(
                await StateAsync(profileId, service, store, cancellationToken));
        }
        catch (DeploymentProfileValidationException exception)
        {
            return TypedResults.Problem(
                new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Deployment profile validation failed",
                    Detail = "Correct the typed deployment profile and try again.",
                    Extensions =
                    {
                        ["code"] = "deployment.profile_invalid",
                        ["errors"] = exception.Errors,
                    },
                });
        }
    }

    private static async Task<IResult> GetAsync(
        Guid profileId,
        IDeploymentProfileService service,
        IDeploymentProfileStore store,
        CancellationToken cancellationToken)
    {
        var document = await service.GetAsync(profileId, cancellationToken);
        return document is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(
                await StateAsync(
                    profileId,
                    service,
                    store,
                    cancellationToken));
    }

    private static async Task<IResult> PreviewAsync(
        Guid profileId,
        PreviewDeploymentProfileRequest request,
        IDeploymentProfileService service,
        IAzureTokenSessionStore sessions,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = sessions.Retrieve(user);
            return TypedResults.Ok(
                await service.PreviewAsync(
                    profileId,
                    request.PrerequisiteRunId,
                    Actor(user),
                    cancellationToken));
        }
        catch (AzureSignInRequiredException)
        {
            sessions.Remove(user);
            return AzureEndpoints.SignInRequiredProblem(httpContext);
        }
        catch (DeploymentProfileNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (DeploymentPrerequisiteException exception)
        {
            return TypedResults.Problem(
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Prerequisite run rejected",
                    Detail = exception.Message,
                    Extensions =
                    {
                        ["code"] = exception.Code,
                        ["blockingCheckIds"] = exception.BlockingCheckIds,
                    },
                });
        }
        catch (DeploymentProfileConflictException exception)
        {
            return Conflict(exception.Code, exception.Message);
        }
        catch (ArmDeploymentPreviewException exception)
        {
            var status = exception.StatusCode is
                StatusCodes.Status400BadRequest
                or StatusCodes.Status403Forbidden
                ? exception.StatusCode
                : StatusCodes.Status502BadGateway;
            return TypedResults.Problem(
                statusCode: status,
                title: "Azure deployment preview failed",
                detail: exception.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = exception.Code,
                });
        }
    }

    private static async Task<IResult> ApproveAsync(
        Guid profileId,
        ApproveDeploymentProfileRequest request,
        IDeploymentProfileService service,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(
                await service.ApproveAsync(
                    profileId,
                    request.PreviewId,
                    Actor(user),
                    cancellationToken));
        }
        catch (DeploymentProfileNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (DeploymentProfileConflictException exception)
        {
            return Conflict(exception.Code, exception.Message);
        }
    }

    private static async Task<DeploymentProfileStateResponse> StateAsync(
        Guid profileId,
        IDeploymentProfileService service,
        IDeploymentProfileStore store,
        CancellationToken cancellationToken) =>
        new(
            (await service.GetAsync(profileId, cancellationToken))!,
            await store.GetPreviewAsync(profileId, cancellationToken),
            await store.GetApprovalAsync(profileId, cancellationToken));

    private static DeploymentActor Actor(ClaimsPrincipal user) =>
        new(
            RequiredClaim(user, "tid"),
            RequiredClaim(user, "oid"));

    private static string RequiredClaim(
        ClaimsPrincipal user,
        string claimType) =>
        user.FindFirstValue(claimType)
        ?? throw new InvalidOperationException(
            $"The authenticated identity has no '{claimType}' claim.");

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Conflict(
        string code,
        string detail) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Deployment profile state changed",
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed class SaveDeploymentProfileRequest
{
    [Required]
    [MaxLength(200)]
    public required string Name { get; init; }

    public Guid SubscriptionId { get; init; }

    [Required]
    [MaxLength(90)]
    public required string ResourceGroupName { get; init; }

    [Required]
    [MaxLength(64)]
    public required string DeploymentName { get; init; }

    [Required]
    [MaxLength(32)]
    public required string Location { get; init; }

    [Required]
    [MaxLength(30)]
    public required string KubernetesVersion { get; init; }

    [Required]
    [MaxLength(100)]
    public required string NodeVmSku { get; init; }

    public int NodeCount { get; init; }

    public int RequestedRegionalVcpu { get; init; }

    [Required]
    [MaxLength(63)]
    public required string AksName { get; init; }

    [Required]
    [MaxLength(80)]
    public required string VnetName { get; init; }

    [Required]
    [MaxLength(80)]
    public required string SubnetName { get; init; }

    [Required]
    [MaxLength(18)]
    public required string VnetCidr { get; init; }

    [Required]
    [MaxLength(18)]
    public required string AksSubnetCidr { get; init; }

    [Required]
    [MaxLength(18)]
    public required string PodCidr { get; init; }

    [Required]
    [MaxLength(18)]
    public required string ServiceCidr { get; init; }

    [Required]
    [MaxLength(15)]
    public required string DnsServiceIp { get; init; }

    [Required]
    [MaxLength(253)]
    public required string PrivateDnsZoneName { get; init; }

    [MaxLength(2_048)]
    public string? ExistingVnetResourceId { get; init; }

    [MaxLength(2_048)]
    public string? ExistingContainerRegistryResourceId { get; init; }

    [MaxLength(2_048)]
    public string? ExistingKeyVaultResourceId { get; init; }

    public DeploymentProfile ToProfile(Guid profileId) =>
        new(
            profileId,
            Name.Trim(),
            SubscriptionId,
            ResourceGroupName.Trim(),
            DeploymentName.Trim(),
            Location.Trim(),
            KubernetesVersion.Trim(),
            NodeVmSku.Trim(),
            NodeCount,
            RequestedRegionalVcpu,
            AksName.Trim(),
            VnetName.Trim(),
            SubnetName.Trim(),
            VnetCidr.Trim(),
            AksSubnetCidr.Trim(),
            PodCidr.Trim(),
            ServiceCidr.Trim(),
            DnsServiceIp.Trim(),
            PrivateDnsZoneName.Trim(),
            EmptyToNull(ExistingVnetResourceId),
            EmptyToNull(ExistingContainerRegistryResourceId),
            EmptyToNull(ExistingKeyVaultResourceId));

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class PreviewDeploymentProfileRequest
{
    public Guid PrerequisiteRunId { get; init; }
}

public sealed class ApproveDeploymentProfileRequest
{
    public Guid PreviewId { get; init; }
}

public sealed record DeploymentProfileStateResponse(
    DeploymentProfileDocument Document,
    DeploymentPreview? Preview,
    DeploymentApproval? Approval);
