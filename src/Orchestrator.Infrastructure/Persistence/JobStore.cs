using System.Data;
using Fsp.Orchestrator.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

internal sealed class JobStore(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    IPersistenceValueGuard valueGuard)
    : IJobStore
{
    public async Task<IReadOnlyList<JobInfo>> ListForDeploymentAsync(
        Guid deploymentId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var jobs = await context.Jobs
            .AsNoTracking()
            .Where(job => job.DeploymentId == deploymentId)
            .ToArrayAsync(cancellationToken);
        return jobs
            .OrderByDescending(job => job.UpdatedAt)
            .Select(Map)
            .ToArray();
    }

    public async Task<JobInfo> CreateAsync(
        CreateJob request,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(
            [request.Kind, request.IdempotencyKey, .. request.Steps]);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var existing = await context.Jobs.SingleOrDefaultAsync(
            job => job.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Map(existing);
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new JobEntity
        {
            Id = request.Id,
            DeploymentId = request.DeploymentId,
            Kind = request.Kind,
            IdempotencyKey = request.IdempotencyKey,
            Status = JobStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        entity.Steps.AddRange(
            request.Steps.Select(
                (name, sequence) => new JobStepEntity
                {
                    Id = Guid.NewGuid(),
                    Sequence = sequence,
                    Name = name,
                    Status = JobStatus.Pending,
                    AttemptCount = 0,
                }));
        context.Jobs.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<JobInfo> TransitionAsync(
        TransitionJob request,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(request.SafeOutput);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var entity = await context.Jobs.SingleOrDefaultAsync(
            job => job.Id == request.JobId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Job '{request.JobId}' was not found.");
        if (entity.Version != request.ExpectedVersion)
        {
            throw new PersistenceConcurrencyException(
                $"Job '{request.JobId}' changed after version {request.ExpectedVersion}.");
        }

        EnsureValidTransition(entity.Status, request.TargetStatus);
        entity.Status = request.TargetStatus;
        entity.SafeOutput = request.SafeOutput;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.Version++;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConcurrencyException(
                $"Job '{request.JobId}' was updated concurrently.",
                exception);
        }

        return Map(entity);
    }

    private static void EnsureValidTransition(JobStatus current, JobStatus target)
    {
        var allowed = current switch
        {
            JobStatus.Pending => target is JobStatus.Running or JobStatus.Cancelled,
            JobStatus.Running => target is JobStatus.Succeeded
                or JobStatus.Failed
                or JobStatus.Cancelled,
            JobStatus.Failed => target == JobStatus.Running,
            _ => false,
        };
        if (!allowed)
        {
            throw new InvalidJobTransitionException(
                $"Job transition from {current} to {target} is not allowed.");
        }
    }

    private static JobInfo Map(JobEntity entity) =>
        new(
            entity.Id,
            entity.DeploymentId,
            entity.Kind,
            entity.IdempotencyKey,
            entity.Status,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.Version);
}
