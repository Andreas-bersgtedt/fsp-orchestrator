using Microsoft.AspNetCore.Http.HttpResults;

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

        return endpoints;
    }
}

/// <summary>Represents the health of the local Orchestrator process.</summary>
public sealed record HealthResponse(string Status, DateTimeOffset CheckedAt);
