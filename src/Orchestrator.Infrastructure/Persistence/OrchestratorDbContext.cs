using Microsoft.EntityFrameworkCore;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

public sealed class OrchestratorDbContext(
    DbContextOptions<OrchestratorDbContext> options)
    : DbContext(options)
{
    internal DbSet<DeploymentEntity> Deployments => Set<DeploymentEntity>();

    internal DbSet<ObservationEntity> Observations => Set<ObservationEntity>();

    internal DbSet<JobEntity> Jobs => Set<JobEntity>();

    internal DbSet<JobStepEntity> JobSteps => Set<JobStepEntity>();

    internal DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    internal DbSet<PrerequisiteRunEntity> PrerequisiteRuns =>
        Set<PrerequisiteRunEntity>();

    internal DbSet<PrerequisiteResultEntity> PrerequisiteResults =>
        Set<PrerequisiteResultEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeploymentEntity>(
            entity =>
            {
                entity.ToTable("Deployments");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Name).HasMaxLength(200);
                entity.Property(item => item.Topology).HasMaxLength(50);
                entity.Property(item => item.ManagerEndpoint).HasMaxLength(2048);
                entity.Property(item => item.AzureResourceId).HasMaxLength(2048);
                entity.Property(item => item.Version).IsConcurrencyToken();
                entity.HasIndex(item => item.ManagerEndpoint).IsUnique();
            });
        modelBuilder.Entity<ObservationEntity>(
            entity =>
            {
                entity.ToTable("Observations");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Component).HasMaxLength(100);
                entity.Property(item => item.Version).HasMaxLength(100);
                entity.Property(item => item.Status).HasMaxLength(50);
                entity.HasIndex(item => new { item.DeploymentId, item.ObservedAt });
                entity.HasOne(item => item.Deployment)
                    .WithMany(item => item.Observations)
                    .HasForeignKey(item => item.DeploymentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        modelBuilder.Entity<JobEntity>(
            entity =>
            {
                entity.ToTable("Jobs");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Kind).HasMaxLength(100);
                entity.Property(item => item.IdempotencyKey).HasMaxLength(200);
                entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(30);
                entity.Property(item => item.SafeOutput).HasMaxLength(16_384);
                entity.Property(item => item.Version).IsConcurrencyToken();
                entity.HasIndex(item => item.IdempotencyKey).IsUnique();
                entity.HasOne(item => item.Deployment)
                    .WithMany(item => item.Jobs)
                    .HasForeignKey(item => item.DeploymentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        modelBuilder.Entity<JobStepEntity>(
            entity =>
            {
                entity.ToTable("JobSteps");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Name).HasMaxLength(200);
                entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(30);
                entity.Property(item => item.SafeOutput).HasMaxLength(16_384);
                entity.HasIndex(item => new { item.JobId, item.Sequence }).IsUnique();
                entity.HasOne(item => item.Job)
                    .WithMany(item => item.Steps)
                    .HasForeignKey(item => item.JobId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        modelBuilder.Entity<AuditEventEntity>(
            entity =>
            {
                entity.ToTable("AuditEvents");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.ActorObjectId).HasMaxLength(100);
                entity.Property(item => item.TenantId).HasMaxLength(100);
                entity.Property(item => item.Action).HasMaxLength(200);
                entity.Property(item => item.TargetType).HasMaxLength(100);
                entity.Property(item => item.TargetId).HasMaxLength(200);
                entity.Property(item => item.Result).HasMaxLength(100);
                entity.Property(item => item.CorrelationId).HasMaxLength(100);
                entity.Property(item => item.InputHash).HasMaxLength(128);
                entity.HasIndex(item => item.OccurredAt);
                entity.HasIndex(item => item.CorrelationId);
            });
        modelBuilder.Entity<PrerequisiteRunEntity>(
            entity =>
            {
                entity.ToTable("PrerequisiteRuns");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Operation).HasMaxLength(100);
                entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(30);
                entity.Property(item => item.InputFingerprint).HasMaxLength(64);
                entity.HasIndex(item => new { item.Operation, item.CreatedAt });
            });
        modelBuilder.Entity<PrerequisiteResultEntity>(
            entity =>
            {
                entity.ToTable("PrerequisiteResults");
                entity.HasKey(item => new { item.RunId, item.CheckId });
                entity.Property(item => item.CheckId).HasMaxLength(150);
                entity.Property(item => item.Category).HasMaxLength(100);
                entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(30);
                entity.Property(item => item.Code).HasMaxLength(150);
                entity.Property(item => item.Summary).HasMaxLength(2_048);
                entity.Property(item => item.EvidenceJson).HasMaxLength(32_768);
                entity.Property(item => item.Remediation).HasMaxLength(4_096);
                entity.Property(item => item.InputFingerprint).HasMaxLength(64);
                entity.HasOne(item => item.Run)
                    .WithMany(item => item.Results)
                    .HasForeignKey(item => item.RunId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAppendOnlyAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyAudit();
        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
    }

    private void EnforceAppendOnlyAudit()
    {
        if (ChangeTracker.Entries<AuditEventEntity>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Audit events are append-only and cannot be changed or deleted.");
        }
    }
}
