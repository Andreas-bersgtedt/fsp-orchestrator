using Fsp.Orchestrator.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

internal sealed class AuditMetadataStore(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    IPersistenceValueGuard valueGuard)
    : IAuditMetadataStore
{
    public async Task<AuditMetadata> AppendAsync(
        AppendAuditMetadata request,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(
            request.ActorObjectId,
            request.TenantId,
            request.Action,
            request.TargetType,
            request.TargetId,
            request.Result,
            request.CorrelationId,
            request.InputHash);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var entity = new AuditEventEntity
        {
            OccurredAt = request.OccurredAt,
            ActorObjectId = request.ActorObjectId,
            TenantId = request.TenantId,
            Action = request.Action,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            Result = request.Result,
            CorrelationId = request.CorrelationId,
            InputHash = request.InputHash,
        };
        context.AuditEvents.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return new AuditMetadata(
            entity.Id,
            entity.OccurredAt,
            entity.ActorObjectId,
            entity.TenantId,
            entity.Action,
            entity.TargetType,
            entity.TargetId,
            entity.Result,
            entity.CorrelationId,
            entity.InputHash);
    }
}
