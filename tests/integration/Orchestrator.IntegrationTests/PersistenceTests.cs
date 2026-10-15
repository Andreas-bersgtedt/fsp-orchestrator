using System.Text;
using System.Security.Claims;
using Fsp.Orchestrator.Api.Azure;
using Fsp.Orchestrator.Application.Deployments;
using Fsp.Orchestrator.Application.Persistence;
using Fsp.Orchestrator.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task MigrationCreatesHealthyDatabase()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();

        var diagnostics = await fixture.Maintenance.GetDiagnosticsAsync(
            CancellationToken.None);

        Assert.Equal("ok", diagnostics.Integrity);
        Assert.NotEqual("none", diagnostics.Migration);
        Assert.Equal("orchestrator.db", diagnostics.DatabaseFileName);
    }

    [Fact]
    public async Task JobCreationIsIdempotent()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var request = new CreateJob(
            Guid.NewGuid(),
            null,
            "deployment.preview",
            "same-request",
            ["validate", "preview"]);

        var first = await fixture.Jobs.CreateAsync(request, CancellationToken.None);
        var second = await fixture.Jobs.CreateAsync(
            request with { Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Version, second.Version);
    }

    [Fact]
    public async Task ConcurrentTransitionsAllowOnlyOneExpectedVersion()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var job = await fixture.Jobs.CreateAsync(
            new CreateJob(
                Guid.NewGuid(),
                null,
                "deployment.apply",
                Guid.NewGuid().ToString(),
                ["apply"]),
            CancellationToken.None);

        var transitions = new[]
        {
            fixture.Jobs.TransitionAsync(
                new TransitionJob(job.Id, JobStatus.Running, job.Version, null),
                CancellationToken.None),
            fixture.Jobs.TransitionAsync(
                new TransitionJob(job.Id, JobStatus.Cancelled, job.Version, null),
                CancellationToken.None),
        };
        var results = await Task.WhenAll(
            transitions.Select(
                async transition =>
                {
                    try
                    {
                        return (Succeeded: true, Error: (Exception?)null, Job: await transition);
                    }
                    catch (Exception error)
                    {
                        return (Succeeded: false, Error: error, Job: (JobInfo?)null);
                    }
                }));

        Assert.Single(results, result => result.Succeeded);
        var failure = Assert.Single(results, result => !result.Succeeded);
        Assert.IsType<PersistenceConcurrencyException>(failure.Error);
    }

    [Fact]
    public async Task SecretValueIsRejectedBeforeSQLiteWrite()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        const string secret = "Bearer abcdefghijklmnopqrstuvwxyz";

        await Assert.ThrowsAsync<UnsafePersistenceValueException>(
            () => fixture.Audit.AppendAsync(
                new AppendAuditMetadata(
                    DateTimeOffset.UtcNow,
                    Guid.NewGuid().ToString(),
                    Guid.NewGuid().ToString(),
                    "job.failed",
                    "job",
                    Guid.NewGuid().ToString(),
                    secret,
                    Guid.NewGuid().ToString(),
                    "hash"),
                CancellationToken.None));

        SqliteConnection.ClearAllPools();
        var bytes = await File.ReadAllBytesAsync(fixture.DatabasePath);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task AuditRowsCannotBeChangedOrDeleted()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var audit = await fixture.Audit.AppendAsync(
            new AppendAuditMetadata(
                DateTimeOffset.UtcNow,
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "database.backup",
                "database",
                "orchestrator.db",
                "succeeded",
                Guid.NewGuid().ToString(),
                "hash"),
            CancellationToken.None);
        await using var context = await fixture.ContextFactory.CreateDbContextAsync();
        var entity = await context.AuditEvents.SingleAsync(
            item => item.Id == audit.Id);
        entity.Result = "changed";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task BackupRestoreReturnsToApprovedMetadata()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var deploymentId = Guid.NewGuid();
        await fixture.Inventory.UpsertAsync(
            new UpsertDeployment(
                deploymentId,
                "Before backup",
                "Enterprise",
                new Uri("https://manager.example.test"),
                null),
            CancellationToken.None);
        var backup = await fixture.Maintenance.CreateBackupAsync(
            CancellationToken.None);
        await fixture.Inventory.UpsertAsync(
            new UpsertDeployment(
                deploymentId,
                "After backup",
                "Enterprise",
                new Uri("https://manager.example.test"),
                null),
            CancellationToken.None);

        await fixture.Maintenance.RestoreBackupAsync(
            backup.FileName,
            backup.Sha256,
            CancellationToken.None);

        SqliteConnection.ClearAllPools();
        await using var context = await fixture.ContextFactory.CreateDbContextAsync();
        var restored = await context.Deployments.SingleAsync(
            item => item.Id == deploymentId);
        Assert.Equal("Before backup", restored.Name);
    }

    [Fact]
    public async Task RegistrationPersistsReferenceAndVersionsAtomically()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var endpoint = new Uri("https://manager.example.test/");
        var credentialReference = new Uri(
            "https://example.vault.azure.net/secrets/manager-token");
        var request = new RegisterDeployment(
            Guid.NewGuid(),
            "Enterprise",
            "Enterprise",
            endpoint,
            "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/fsp",
            "Bearer",
            credentialReference,
            [
                new ObservedComponent("manager", "3.1.0", "healthy"),
                new ObservedComponent("agent-a", "3.1.0", "observed"),
            ],
            DateTimeOffset.UtcNow);

        var deployment = await fixture.Inventory.RegisterAsync(
            request,
            CancellationToken.None);

        Assert.Equal(credentialReference, deployment.CredentialReference);
        Assert.True(
            await fixture.Inventory.ExistsByManagerEndpointAsync(
                endpoint,
                CancellationToken.None));
        await using var context = await fixture.ContextFactory.CreateDbContextAsync();
        var observations = await context.Observations
            .Where(item => item.DeploymentId == request.Id)
            .ToArrayAsync();
        Assert.Equal(2, observations.Length);
        await Assert.ThrowsAsync<DuplicateManagerEndpointException>(
            () => fixture.Inventory.RegisterAsync(
                request with { Id = Guid.NewGuid() },
                CancellationToken.None));
        Assert.Single(await context.Deployments.ToArrayAsync());
    }

    [Fact]
    public async Task FailedAttemptRetainsLastSuccessfulObservationTimestamp()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var deployment = await fixture.Inventory.UpsertAsync(
            new UpsertDeployment(
                Guid.NewGuid(),
                "Observed",
                "Lite",
                new Uri("https://lite.example.test"),
                null),
            CancellationToken.None);
        var successfulAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await fixture.Observations.RecordAttemptAsync(
            new DeploymentObservationAttempt(
                Guid.NewGuid(),
                deployment.Id,
                DeploymentObservationStatus.Healthy,
                "observation.healthy",
                successfulAt.AddSeconds(-1),
                successfulAt,
                [new ObservedComponent("health", "ok", "current")]),
            CancellationToken.None);
        await fixture.Observations.RecordAttemptAsync(
            new DeploymentObservationAttempt(
                Guid.NewGuid(),
                deployment.Id,
                DeploymentObservationStatus.Unreachable,
                "observation.unreachable",
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow,
                []),
            CancellationToken.None);

        var state = await fixture.Observations.GetStateAsync(
            deployment.Id,
            CancellationToken.None);

        Assert.Equal(
            DeploymentObservationStatus.Unreachable,
            state!.LatestAttempt!.Status);
        Assert.Equal(successfulAt, state.LastSuccessfulAt);
        Assert.Equal("ok", Assert.Single(state.LastSuccessfulValues).Version);
    }

    [Fact]
    public async Task MissingAzureTokenPausesRunningJob()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var pending = await fixture.Jobs.CreateAsync(
            new CreateJob(
                Guid.NewGuid(),
                null,
                "azure.discovery",
                Guid.NewGuid().ToString(),
                ["discover"]),
            CancellationToken.None);
        var running = await fixture.Jobs.TransitionAsync(
            new TransitionJob(
                pending.Id,
                JobStatus.Running,
                pending.Version,
                null),
            CancellationToken.None);
        var provider = new AzureJobTokenProvider(
            new InMemoryAzureTokenSessionStore(TimeProvider.System),
            fixture.Jobs);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        "tid",
                        "00000000-0000-0000-0000-000000000001"),
                    new Claim(
                        "oid",
                        "00000000-0000-0000-0000-000000000002"),
                ],
                "test"));

        await Assert.ThrowsAsync<AzureSignInRequiredException>(
            () => provider.RequireAsync(
                running,
                principal,
                CancellationToken.None));

        await using var context = await fixture.ContextFactory.CreateDbContextAsync();
        var entity = await context.Jobs.SingleAsync(job => job.Id == running.Id);
        Assert.Equal(JobStatus.Paused, entity.Status);
        Assert.Equal("azure.sign_in_required", entity.SafeOutput);
    }

    private sealed class PersistenceFixture : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly string directory;

        private PersistenceFixture(ServiceProvider provider, string directory)
        {
            this.provider = provider;
            this.directory = directory;
            DatabasePath = Path.Combine(directory, "orchestrator.db");
        }

        public string DatabasePath { get; }

        public IInventoryStore Inventory =>
            provider.GetRequiredService<IInventoryStore>();

        public IJobStore Jobs => provider.GetRequiredService<IJobStore>();

        public IDeploymentObservationStore Observations =>
            provider.GetRequiredService<IDeploymentObservationStore>();

        public IAuditMetadataStore Audit =>
            provider.GetRequiredService<IAuditMetadataStore>();

        public IDatabaseMaintenance Maintenance =>
            provider.GetRequiredService<IDatabaseMaintenance>();

        public IDbContextFactory<OrchestratorDbContext> ContextFactory =>
            provider.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>();

        public static async Task<PersistenceFixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"fsp-persistence-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var services = new ServiceCollection();
            services.AddOrchestratorPersistence(
                Path.Combine(directory, "orchestrator.db"));
            var provider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true,
                });
            var fixture = new PersistenceFixture(provider, directory);
            await fixture.Maintenance.MigrateAsync(CancellationToken.None);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
