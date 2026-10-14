using Microsoft.AspNetCore.Http.HttpResults;
using Fsp.Orchestrator.Api.Hosting;

namespace Fsp.Orchestrator.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/health",
                Ok<HealthResponse> (CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return TypedResults.Ok(
                        new HealthResponse("ok", DateTimeOffset.UtcNow));
                })
            .WithName("GetOrchestratorHealth")
            .WithSummary("Get Orchestrator health")
            .WithDescription("Returns the local Orchestrator process health and current UTC time.")
            .Produces<HealthResponse>(StatusCodes.Status200OK);

        endpoints.MapGet(
                "/api/ready",
                Results<Ok<ReadinessResponse>, ProblemHttpResult> (
                    StartupReadiness readiness,
                    HostingRuntime hostingRuntime,
                    CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!readiness.IsReady)
                    {
                        return TypedResults.Problem(
                            statusCode: StatusCodes.Status503ServiceUnavailable,
                            title: "Orchestrator is not ready",
                            detail: "The HTTPS listener and local state are still starting.");
                    }

                    return TypedResults.Ok(
                        new ReadinessResponse(
                            "ready",
                            hostingRuntime.Origin,
                            hostingRuntime.CertificateExpiresAt,
                            DateTimeOffset.UtcNow));
                })
            .WithName("GetOrchestratorReadiness")
            .WithSummary("Get Orchestrator readiness")
            .WithDescription(
                "Reports ready only after the loopback HTTPS listener, certificate, "
                + "and local state directory have initialized.")
            .Produces<ReadinessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}

/// <summary>Represents the health of the local Orchestrator process.</summary>
public sealed record HealthResponse(string Status, DateTimeOffset CheckedAt);

/// <summary>Represents startup readiness for browser and service traffic.</summary>
public sealed record ReadinessResponse(
    string Status,
    string Origin,
    DateTimeOffset CertificateExpiresAt,
    DateTimeOffset CheckedAt);
