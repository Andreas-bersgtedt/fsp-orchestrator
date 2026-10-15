using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Application.Prerequisites;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Fsp.Orchestrator.Api.Endpoints;

public static class PrerequisiteEndpoints
{
    public static RouteGroupBuilder MapPrerequisites(
        this RouteGroupBuilder endpoints)
    {
        endpoints.MapGet(
                "/prerequisites/catalog",
                Ok<PrerequisiteCatalogResponse> (
                    IPrerequisiteEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return TypedResults.Ok(
                        new PrerequisiteCatalogResponse(
                            engine.Catalog.Select(Map).ToArray()));
                })
            .RequireAuthorization(OrchestratorRoles.Reader)
            .WithName("GetPrerequisiteCatalog")
            .WithSummary("Get prerequisite checker catalog")
            .WithDescription("Returns stable typed checker definitions and dependencies.")
            .Produces<PrerequisiteCatalogResponse>(StatusCodes.Status200OK);

        endpoints.MapPost(
                "/prerequisites/runs",
                async Task<Accepted<StartPrerequisiteRunResponse>> (
                    StartPrerequisiteRunRequest request,
                    IPrerequisiteRunCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                {
                    var runId = Guid.NewGuid();
                    await coordinator.StartAsync(
                        new PrerequisiteRunRequest(
                            runId,
                            request.Operation,
                            request.Inputs,
                            request.CheckIds),
                        cancellationToken);
                    return TypedResults.Accepted(
                        $"/api/prerequisites/runs/{runId}",
                        new StartPrerequisiteRunResponse(runId));
                })
            .RequireAuthorization(OrchestratorRoles.Operator)
            .WithName("StartPrerequisiteRun")
            .WithSummary("Start a prerequisite run")
            .WithDescription(
                "Starts dependency-aware checks and returns a persisted run ID.")
            .Produces<StartPrerequisiteRunResponse>(StatusCodes.Status202Accepted);

        endpoints.MapGet(
                "/prerequisites/runs/{runId:guid}",
                async Task<Results<Ok<PrerequisiteRunResponse>, NotFound>> (
                    Guid runId,
                    IPrerequisiteRunStore store,
                    CancellationToken cancellationToken) =>
                {
                    var run = await store.GetAsync(runId, cancellationToken);
                    return run is null
                        ? TypedResults.NotFound()
                        : TypedResults.Ok(Map(run));
                })
            .RequireAuthorization(OrchestratorRoles.Reader)
            .WithName("GetPrerequisiteRun")
            .WithSummary("Get prerequisite run")
            .WithDescription("Returns current persisted run and check results.")
            .Produces<PrerequisiteRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPost(
                "/prerequisites/runs/{runId:guid}/cancel",
                Results<Accepted, NotFound> (
                    Guid runId,
                    IPrerequisiteRunCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return coordinator.Cancel(runId)
                        ? TypedResults.Accepted(
                            $"/api/prerequisites/runs/{runId}")
                        : TypedResults.NotFound();
                })
            .RequireAuthorization(OrchestratorRoles.Operator)
            .WithName("CancelPrerequisiteRun")
            .WithSummary("Cancel prerequisite run")
            .WithDescription("Cancels an active run and persists cancelled results.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPost(
                "/prerequisites/runs/{runId:guid}/validate",
                async Task<Results<
                    Ok<PrerequisiteGateResponse>,
                    Conflict<PrerequisiteGateResponse>>> (
                    Guid runId,
                    ValidatePrerequisiteRunRequest request,
                    IPrerequisiteGate gate,
                    CancellationToken cancellationToken) =>
                {
                    var result = await gate.ValidateAsync(
                        runId,
                        request.Operation,
                        request.Inputs,
                        cancellationToken);
                    var response = new PrerequisiteGateResponse(
                        result.Allowed,
                        result.Code,
                        result.Message,
                        result.BlockingCheckIds);
                    return result.Allowed
                        ? TypedResults.Ok(response)
                        : TypedResults.Conflict(response);
                })
            .RequireAuthorization(OrchestratorRoles.Operator)
            .WithName("ValidatePrerequisiteRun")
            .WithSummary("Validate prerequisite run for apply")
            .WithDescription(
                "Rejects missing, incomplete, failed, stale, expired, or operation-mismatched runs.")
            .Produces<PrerequisiteGateResponse>(StatusCodes.Status200OK)
            .Produces<PrerequisiteGateResponse>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static PrerequisiteCheckDefinitionResponse Map(
        PrerequisiteCheckDefinition definition) =>
        new(
            definition.Id,
            definition.Category,
            definition.DisplayName,
            definition.Description,
            definition.Dependencies,
            definition.Operations,
            definition.InputKeys,
            (long)definition.Timeout.TotalMilliseconds,
            (long)definition.Lifetime.TotalSeconds,
            definition.Blocking,
            definition.Version);

    private static PrerequisiteRunResponse Map(PrerequisiteRun run) =>
        new(
            run.Id,
            run.Operation,
            run.Status.ToString(),
            run.InputFingerprint,
            run.CreatedAt,
            run.StartedAt,
            run.CompletedAt,
            run.Results
                .Select(
                    result => new PrerequisiteCheckResultResponse(
                        result.CheckId,
                        result.Category,
                        result.Blocking,
                        result.Status.ToString(),
                        result.Code,
                        result.Summary,
                        result.Evidence,
                        result.Remediation,
                        result.InputFingerprint,
                        result.StartedAt,
                        result.CompletedAt,
                        result.ExpiresAt,
                        result.DurationMilliseconds))
                .ToArray());
}

/// <summary>All prerequisite checks available in the current build.</summary>
public sealed record PrerequisiteCatalogResponse(
    IReadOnlyList<PrerequisiteCheckDefinitionResponse> Checks);

/// <summary>Stable checker definition returned by the catalog.</summary>
public sealed record PrerequisiteCheckDefinitionResponse(
    string Id,
    string Category,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Operations,
    IReadOnlyList<string> InputKeys,
    long TimeoutMilliseconds,
    long LifetimeSeconds,
    bool Blocking,
    string Version);

/// <summary>Input for starting an asynchronous prerequisite run.</summary>
public sealed record StartPrerequisiteRunRequest
{
    public required string Operation { get; init; }

    public required IReadOnlyDictionary<string, string> Inputs { get; init; }

    public IReadOnlyList<string>? CheckIds { get; init; }
}

/// <summary>Location identifier returned when a prerequisite run starts.</summary>
public sealed record StartPrerequisiteRunResponse(Guid RunId);

/// <summary>Current persisted prerequisite run state.</summary>
public sealed record PrerequisiteRunResponse(
    Guid Id,
    string Operation,
    string Status,
    string InputFingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<PrerequisiteCheckResultResponse> Results);

/// <summary>Current persisted result for one prerequisite check.</summary>
public sealed record PrerequisiteCheckResultResponse(
    string CheckId,
    string Category,
    bool Blocking,
    string Status,
    string Code,
    string Summary,
    IReadOnlyDictionary<string, string> Evidence,
    string Remediation,
    string InputFingerprint,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    long DurationMilliseconds);

/// <summary>Current operation inputs used to validate a completed run.</summary>
public sealed record ValidatePrerequisiteRunRequest
{
    public required string Operation { get; init; }

    public required IReadOnlyDictionary<string, string> Inputs { get; init; }
}

/// <summary>Apply-gate decision for a prerequisite run.</summary>
public sealed record PrerequisiteGateResponse(
    bool Allowed,
    string Code,
    string Message,
    IReadOnlyList<string> BlockingCheckIds);
