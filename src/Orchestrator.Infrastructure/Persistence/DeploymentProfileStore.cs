using System.Text.Json;
using System.Text.Json.Serialization;
using Fsp.Orchestrator.Application.Deployments;
using Fsp.Orchestrator.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

internal sealed class DeploymentProfileStore(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    IPersistenceValueGuard valueGuard)
    : IDeploymentProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<IReadOnlyList<DeploymentProfileDocument>> ListAsync(
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var entities = await context.DeploymentProfiles
            .AsNoTracking()
            .OrderByDescending(item => item.UpdatedAt)
            .Take(50)
            .ToArrayAsync(cancellationToken);
        return entities.Select(Map).ToArray();
    }

    public async Task<DeploymentProfileDocument> SaveAsync(
        DeploymentProfile profile,
        string fingerprint,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken)
    {
        var profileJson = JsonSerializer.Serialize(profile, JsonOptions);
        valueGuard.EnsureSafe(profileJson, fingerprint);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var entity = await context.DeploymentProfiles.FindAsync(
            [profile.Id],
            cancellationToken);
        if (entity is null)
        {
            entity = new DeploymentProfileEntity
            {
                Id = profile.Id,
                ProfileJson = profileJson,
                Fingerprint = fingerprint,
                Revision = 1,
                CreatedAt = savedAt,
                UpdatedAt = savedAt,
            };
            context.DeploymentProfiles.Add(entity);
        }
        else
        {
            if (!string.Equals(
                    entity.Fingerprint,
                    fingerprint,
                    StringComparison.Ordinal))
            {
                await InvalidateApprovalAsync(context, entity, savedAt);
                entity.PrerequisiteRunId = null;
                entity.CurrentPreviewId = null;
                entity.CurrentApprovalId = null;
            }

            entity.ProfileJson = profileJson;
            entity.Fingerprint = fingerprint;
            entity.Revision++;
            entity.UpdatedAt = savedAt;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<DeploymentProfileDocument?> GetAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var entity = await context.DeploymentProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == profileId,
                cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<DeploymentPreview?> GetPreviewAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var profile = await context.DeploymentProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == profileId,
                cancellationToken);
        if (profile?.CurrentPreviewId is not { } previewId)
        {
            return null;
        }

        var preview = await context.DeploymentPreviews
            .AsNoTracking()
            .Include(item => item.Changes)
            .SingleAsync(item => item.Id == previewId, cancellationToken);
        return Map(preview);
    }

    public async Task<DeploymentApproval?> GetApprovalAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        var profile = await context.DeploymentProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == profileId,
                cancellationToken);
        if (profile?.CurrentApprovalId is not { } approvalId)
        {
            return null;
        }

        var approval = await context.DeploymentApprovals
            .AsNoTracking()
            .SingleAsync(item => item.Id == approvalId, cancellationToken);
        return Map(approval);
    }

    public async Task<DeploymentPreview> SavePreviewAsync(
        Guid profileId,
        string expectedProfileFingerprint,
        Guid prerequisiteRunId,
        ArmDeploymentPreview preview,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(
            [
                preview.ValidationCorrelationId,
                preview.WhatIfCorrelationId,
                .. preview.Changes.SelectMany(
                    item => new[]
                    {
                        item.ResourceId,
                        item.BeforeJson,
                        item.AfterJson,
                        item.DeltaJson,
                    }),
            ]);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var profile = await context.DeploymentProfiles.FindAsync(
            [profileId],
            cancellationToken)
            ?? throw new DeploymentProfileNotFoundException();
        if (!string.Equals(
                profile.Fingerprint,
                expectedProfileFingerprint,
                StringComparison.Ordinal))
        {
            throw new DeploymentProfileConflictException(
                "deployment.profile_changed");
        }

        await InvalidateApprovalAsync(context, profile, createdAt);
        var entity = new DeploymentPreviewEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            ProfileFingerprint = profile.Fingerprint,
            PrerequisiteRunId = prerequisiteRunId,
            ValidationCorrelationId = preview.ValidationCorrelationId,
            WhatIfCorrelationId = preview.WhatIfCorrelationId,
            CreateCount = preview.Changes.Count(
                item => item.ChangeType == DeploymentChangeType.Create),
            ModifyCount = preview.Changes.Count(
                item => item.ChangeType == DeploymentChangeType.Modify),
            DeleteCount = preview.Changes.Count(
                item => item.ChangeType == DeploymentChangeType.Delete),
            CreatedAt = createdAt,
        };
        entity.Changes.AddRange(
            preview.Changes.Select(
                change => new DeploymentWhatIfChangeEntity
                {
                    Id = Guid.NewGuid(),
                    PreviewId = entity.Id,
                    ResourceId = change.ResourceId,
                    ChangeType = change.ChangeType,
                    BeforeJson = change.BeforeJson,
                    AfterJson = change.AfterJson,
                    DeltaJson = change.DeltaJson,
                }));
        context.DeploymentPreviews.Add(entity);
        profile.PrerequisiteRunId = prerequisiteRunId;
        profile.CurrentPreviewId = entity.Id;
        profile.CurrentApprovalId = null;
        profile.UpdatedAt = createdAt;
        profile.Revision++;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<DeploymentApproval> ApproveAsync(
        Guid profileId,
        Guid previewId,
        DeploymentActor actor,
        DateTimeOffset approvedAt,
        CancellationToken cancellationToken)
    {
        valueGuard.EnsureSafe(actor.TenantId, actor.ObjectId);
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var profile = await context.DeploymentProfiles.FindAsync(
            [profileId],
            cancellationToken)
            ?? throw new DeploymentProfileNotFoundException();
        if (profile.CurrentPreviewId != previewId)
        {
            throw new DeploymentProfileConflictException(
                "deployment.preview_stale");
        }

        if (profile.CurrentApprovalId is { } currentApprovalId)
        {
            var current = await context.DeploymentApprovals
                .AsNoTracking()
                .SingleAsync(
                    item => item.Id == currentApprovalId,
                    cancellationToken);
            if (current.PreviewId == previewId
                && current.InvalidatedAt is null)
            {
                return Map(current);
            }
        }

        var preview = await context.DeploymentPreviews
            .SingleAsync(item => item.Id == previewId, cancellationToken);
        if (!string.Equals(
                preview.ProfileFingerprint,
                profile.Fingerprint,
                StringComparison.Ordinal))
        {
            throw new DeploymentProfileConflictException(
                "deployment.profile_changed");
        }

        var approval = new DeploymentApprovalEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            PreviewId = previewId,
            ProfileFingerprint = profile.Fingerprint,
            ActorTenantId = actor.TenantId,
            ActorObjectId = actor.ObjectId,
            CreateCount = preview.CreateCount,
            ModifyCount = preview.ModifyCount,
            DeleteCount = preview.DeleteCount,
            ApprovedAt = approvedAt,
        };
        context.DeploymentApprovals.Add(approval);
        profile.CurrentApprovalId = approval.Id;
        profile.UpdatedAt = approvedAt;
        profile.Revision++;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(approval);
    }

    private static async Task InvalidateApprovalAsync(
        OrchestratorDbContext context,
        DeploymentProfileEntity profile,
        DateTimeOffset invalidatedAt)
    {
        if (profile.CurrentApprovalId is not { } approvalId)
        {
            return;
        }

        var approval = await context.DeploymentApprovals.FindAsync([approvalId]);
        if (approval is not null && approval.InvalidatedAt is null)
        {
            approval.InvalidatedAt = invalidatedAt;
        }
    }

    private static DeploymentProfileDocument Map(
        DeploymentProfileEntity entity) =>
        new(
            JsonSerializer.Deserialize<DeploymentProfile>(
                entity.ProfileJson,
                JsonOptions)
                ?? throw new JsonException("The deployment profile is invalid."),
            entity.Fingerprint,
            entity.Revision,
            entity.PrerequisiteRunId,
            entity.CurrentPreviewId,
            entity.CurrentApprovalId,
            entity.CreatedAt,
            entity.UpdatedAt);

    private static DeploymentPreview Map(DeploymentPreviewEntity entity) =>
        new(
            entity.Id,
            entity.ProfileId,
            entity.ProfileFingerprint,
            entity.PrerequisiteRunId,
            entity.ValidationCorrelationId,
            entity.WhatIfCorrelationId,
            entity.CreateCount,
            entity.ModifyCount,
            entity.DeleteCount,
            entity.Changes
                .OrderBy(item => item.ResourceId, StringComparer.Ordinal)
                .Select(
                    item => new DeploymentWhatIfChange(
                        item.ResourceId,
                        item.ChangeType,
                        item.BeforeJson,
                        item.AfterJson,
                        item.DeltaJson))
                .ToArray(),
            entity.CreatedAt);

    private static DeploymentApproval Map(DeploymentApprovalEntity entity) =>
        new(
            entity.Id,
            entity.ProfileId,
            entity.PreviewId,
            entity.ProfileFingerprint,
            entity.ActorTenantId,
            entity.ActorObjectId,
            entity.CreateCount,
            entity.ModifyCount,
            entity.DeleteCount,
            entity.ApprovedAt,
            entity.InvalidatedAt);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
