using Fsp.Orchestrator.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

internal sealed class InventoryStore(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    IPersistenceValueGuard valueGuard)
    : IInventoryStore
{
    public async Task<IReadOnlyList<DeploymentInventoryItem>> ListAsync(
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var entities = await context.Deployments
            .AsNoTracking()
            .OrderBy(deployment => deployment.Name)
            .ToArrayAsync(cancellationToken);
        return entities.Select(Map).ToArray();
    }

    public async Task<DeploymentInventoryItem?> GetAsync(
        Guid deploymentId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var entity = await context.Deployments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                deployment => deployment.Id == deploymentId,
                cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<bool> ExistsByManagerEndpointAsync(
        Uri managerEndpoint,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var endpoint = managerEndpoint.ToString();
        return await context.Deployments.AnyAsync(
            deployment => deployment.ManagerEndpoint == endpoint,
            cancellationToken);
    }

    public async Task<DeploymentInventoryItem> RegisterAsync(
        RegisterDeployment request,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(
            request.Name,
            request.Topology,
            request.ManagerEndpoint.ToString(),
            request.AzureResourceId,
            request.CredentialKind,
            request.CredentialReference?.ToString());
        foreach (var component in request.Components)
        {
            valueGuard.EnsureSafe(
                component.Name,
                component.Version,
                component.Status);
        }

        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var endpoint = request.ManagerEndpoint.ToString();
        if (await context.Deployments.AnyAsync(
                deployment => deployment.ManagerEndpoint == endpoint,
                cancellationToken))
        {
            throw new DuplicateManagerEndpointException();
        }

        var entity = new DeploymentEntity
        {
            Id = request.Id,
            Name = request.Name,
            Topology = request.Topology,
            ManagerEndpoint = endpoint,
            AzureResourceId = request.AzureResourceId,
            CredentialKind = request.CredentialKind,
            CredentialReference = request.CredentialReference?.ToString(),
            CreatedAt = request.ObservedAt,
            UpdatedAt = request.ObservedAt,
            Version = 1,
        };
        context.Deployments.Add(entity);
        foreach (var component in request.Components)
        {
            context.Observations.Add(
                new ObservationEntity
                {
                    Id = Guid.NewGuid(),
                    DeploymentId = request.Id,
                    Component = component.Name,
                    Version = component.Version,
                    Status = component.Status,
                    ObservedAt = request.ObservedAt,
                });
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new DuplicateManagerEndpointException();
        }

        return Map(entity);
    }

    public async Task<DeploymentInventoryItem> UpsertAsync(
        UpsertDeployment request,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(
            request.Name,
            request.Topology,
            request.ManagerEndpoint.ToString(),
            request.AzureResourceId,
            request.CredentialKind,
            request.CredentialReference?.ToString());
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
                CredentialKind = request.CredentialKind,
                CredentialReference = request.CredentialReference?.ToString(),
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
            entity.CredentialKind = request.CredentialKind;
            entity.CredentialReference = request.CredentialReference?.ToString();
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
            entity.CredentialKind,
            entity.CredentialReference is null
                ? null
                : new Uri(entity.CredentialReference),
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.Version);
}
