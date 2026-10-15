using Fsp.Orchestrator.Application.Deployments;
using Fsp.Orchestrator.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

internal sealed class DeploymentObservationStore(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    IPersistenceValueGuard valueGuard)
    : IDeploymentObservationStore
{
    public async Task RecordAttemptAsync(
        DeploymentObservationAttempt attempt,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(attempt.Code);
        foreach (var value in attempt.Values)
        {
            valueGuard.EnsureSafe(value.Name, value.Version, value.Status);
        }

        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        if (!await context.Deployments.AnyAsync(
                deployment => deployment.Id == attempt.DeploymentId,
                cancellationToken))
        {
            throw new KeyNotFoundException(
                $"Deployment '{attempt.DeploymentId}' is not registered.");
        }

        context.ObservationAttempts.Add(
            new ObservationAttemptEntity
            {
                Id = attempt.Id,
                DeploymentId = attempt.DeploymentId,
                Status = attempt.Status,
                Code = attempt.Code,
                StartedAt = attempt.StartedAt,
                CompletedAt = attempt.CompletedAt,
                CompletedAtUnixMilliseconds =
                    attempt.CompletedAt.ToUnixTimeMilliseconds(),
            });
        foreach (var value in attempt.Values)
        {
            context.Observations.Add(
                new ObservationEntity
                {
                    Id = Guid.NewGuid(),
                    DeploymentId = attempt.DeploymentId,
                    AttemptId = attempt.Id,
                    Component = value.Name,
                    Version = value.Version,
                    Status = value.Status,
                    ObservedAt = attempt.CompletedAt,
                });
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DeploymentObservationState?> GetStateAsync(
        Guid deploymentId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        if (!await context.Deployments.AnyAsync(
                deployment => deployment.Id == deploymentId,
                cancellationToken))
        {
            return null;
        }

        var latest = await context.ObservationAttempts
            .AsNoTracking()
            .Where(attempt => attempt.DeploymentId == deploymentId)
            .OrderByDescending(attempt => attempt.CompletedAtUnixMilliseconds)
            .FirstOrDefaultAsync(cancellationToken);
        var lastSuccessful = await context.ObservationAttempts
            .AsNoTracking()
            .Where(
                attempt => attempt.DeploymentId == deploymentId
                    && attempt.Status == DeploymentObservationStatus.Healthy)
            .OrderByDescending(attempt => attempt.CompletedAtUnixMilliseconds)
            .FirstOrDefaultAsync(cancellationToken);
        var latestValues = latest is null
            ? []
            : await ValuesAsync(context, latest.Id, cancellationToken);
        var successfulValues = lastSuccessful is null
            ? []
            : latest is not null && latest.Id == lastSuccessful.Id
                ? latestValues
                : await ValuesAsync(
                    context,
                    lastSuccessful.Id,
                    cancellationToken);
        return new DeploymentObservationState(
            deploymentId,
            latest is null
                ? null
                : new DeploymentObservationAttempt(
                    latest.Id,
                    latest.DeploymentId,
                    latest.Status,
                    latest.Code,
                    latest.StartedAt,
                    latest.CompletedAt,
                    latestValues),
            lastSuccessful?.CompletedAt,
            successfulValues);
    }

    private static async Task<IReadOnlyList<ObservedComponent>> ValuesAsync(
        OrchestratorDbContext context,
        Guid attemptId,
        CancellationToken cancellationToken) =>
        await context.Observations
            .AsNoTracking()
            .Where(value => value.AttemptId == attemptId)
            .OrderBy(value => value.Component)
            .Select(
                value => new ObservedComponent(
                    value.Component,
                    value.Version,
                    value.Status))
            .ToArrayAsync(cancellationToken);
}
