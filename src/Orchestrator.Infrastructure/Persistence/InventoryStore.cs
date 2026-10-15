using Fsp.Orchestrator.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

internal sealed class InventoryStore(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    IPersistenceValueGuard valueGuard)
    : IInventoryStore
{
    public async Task<DeploymentInventoryItem> UpsertAsync(
        UpsertDeployment request,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(
            request.Name,
            request.Topology,
            request.ManagerEndpoint.ToString(),
            request.AzureResourceId);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var entity = await context.Deployments.FindAsync(
            [request.Id],
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (entity is null)
        {
            entity = new DeploymentEntity
            {
                Id = request.Id,
                Name = request.Name,
                Topology = request.Topology,
                ManagerEndpoint = request.ManagerEndpoint.ToString(),
                AzureResourceId = request.AzureResourceId,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };
            context.Deployments.Add(entity);
        }
        else
        {
            entity.Name = request.Name;
            entity.Topology = request.Topology;
            entity.ManagerEndpoint = request.ManagerEndpoint.ToString();
            entity.AzureResourceId = request.AzureResourceId;
            entity.UpdatedAt = now;
            entity.Version++;
        }

        await context.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<DeploymentObservation> RecordObservationAsync(
        RecordObservation request,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(
            request.Component,
            request.Version,
            request.Status);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        if (!await context.Deployments.AnyAsync(
                deployment => deployment.Id == request.DeploymentId,
                cancellationToken))
        {
            throw new KeyNotFoundException(
                $"Deployment '{request.DeploymentId}' is not registered.");
        }

        var entity = new ObservationEntity
        {
            Id = Guid.NewGuid(),
            DeploymentId = request.DeploymentId,
            Component = request.Component,
            Version = request.Version,
            Status = request.Status,
            ObservedAt = request.ObservedAt,
        };
        context.Observations.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return new DeploymentObservation(
            entity.Id,
            entity.DeploymentId,
            entity.Component,
            entity.Version,
            entity.Status,
            entity.ObservedAt);
    }

    private static DeploymentInventoryItem Map(DeploymentEntity entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.Topology,
            new Uri(entity.ManagerEndpoint),
            entity.AzureResourceId,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.Version);
}
