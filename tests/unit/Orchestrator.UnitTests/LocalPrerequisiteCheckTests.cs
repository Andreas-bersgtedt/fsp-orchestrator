using Fsp.Orchestrator.Application.Prerequisites;

namespace Fsp.Orchestrator.UnitTests;

public sealed class LocalPrerequisiteCheckTests
{
    private const string TenantId = "00000000-0000-0000-0000-000000000001";
    private const string ApiClientId = "00000000-0000-0000-0000-000000000002";
    private const string SpaClientId = "00000000-0000-0000-0000-000000000003";

    [Fact]
    public void CatalogHasStableIdsTimeoutsLifetimesAndErrorMappings()
    {
        Assert.Equal(
            LocalPrerequisiteCatalog.Definitions.Count,
            LocalPrerequisiteCatalog.Definitions
                .Select(definition => definition.Id)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            LocalPrerequisiteCatalog.Definitions,
            definition =>
            {
                Assert.NotEmpty(definition.Id);
                Assert.True(definition.Timeout > TimeSpan.Zero);
                Assert.True(definition.Lifetime > TimeSpan.Zero);
                Assert.Equal("1.0", definition.Version);
            });
    }

    [Fact]
    public async Task HealthySnapshotsPassAndCertificateEvidenceHasNoPrivateKeyData()
    {
        var probe = new FakeProbe();
        var context = Context();

        var outcomes = await Task.WhenAll(
            LocalPrerequisiteCatalog.Definitions.Select(
                definition => new LocalPrerequisiteCheck(definition, probe)
                    .ExecuteAsync(context, CancellationToken.None)));

        Assert.All(
            outcomes,
            outcome => Assert.Equal(
                PrerequisiteCheckStatus.Passed,
                outcome.Status));
        var certificate = outcomes[
            IndexOf("local.https-certificate")];
        Assert.Equal("CN=localhost", certificate.Evidence["subject"]);
        Assert.DoesNotContain(
            certificate.Evidence.Keys,
            key => key.Contains("private", StringComparison.OrdinalIgnoreCase));
        Assert.All(outcomes, outcome => Assert.NotEmpty(outcome.Code));
    }

    [Theory]
    [InlineData(30, "Passed", "local.clock.passed")]
    [InlineData(45, "Warning", "local.clock.skew_detected")]
    [InlineData(301, "Failed", "local.clock.skew_excessive")]
    public async Task ClockReportsMeasuredSkewAndThresholdStatus(
        int skewSeconds,
        string expectedStatus,
        string expectedCode)
    {
        var probe = new FakeProbe
        {
            Clock = new LocalClockSnapshot(
                "trusted.example",
                TimeSpan.FromSeconds(skewSeconds)),
        };

        var outcome = await ExecuteAsync("local.clock", probe);

        Assert.Equal(expectedStatus, outcome.Status.ToString());
        Assert.Equal(expectedCode, outcome.Code);
        Assert.Equal(
            (skewSeconds * 1000).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            outcome.Evidence["skewMilliseconds"]);
    }

    [Fact]
    public async Task DatabaseFailureAndLowSpaceUseStableMappings()
    {
        var integrityFailure = new FakeProbe
        {
            Database = new LocalDatabaseSnapshot(
                "corrupt",
                "migration",
                0,
                2_000_000_000),
        };
        var lowSpace = new FakeProbe
        {
            Database = new LocalDatabaseSnapshot(
                "ok",
                "migration",
                0,
                512_000_000),
        };

        var failed = await ExecuteAsync("local.database", integrityFailure);
        var warning = await ExecuteAsync("local.database", lowSpace);

        Assert.Equal(PrerequisiteCheckStatus.Failed, failed.Status);
        Assert.Equal("local.database.integrity_failed", failed.Code);
        Assert.Equal(PrerequisiteCheckStatus.Warning, warning.Status);
        Assert.Equal("local.database.low_space", warning.Code);
    }

    [Fact]
    public async Task EntraMismatchNamesTheField()
    {
        var probe = new FakeProbe
        {
            Entra = new LocalEntraSnapshot(
                TenantId,
                ApiClientId,
                SpaClientId,
                "https://localhost:9443/wrong",
                "orchestrator.access_as_user",
                $"api://{ApiClientId}/orchestrator.access_as_user"),
        };

        var outcome = await ExecuteAsync("entra.redirect-uri", probe);

        Assert.Equal(PrerequisiteCheckStatus.Failed, outcome.Status);
        Assert.Equal("redirectUri", outcome.Evidence["field"]);
        Assert.Contains(
            "redirectUri",
            outcome.Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationIsObservedByProbeBackedCheck()
    {
        var probe = new FakeProbe { WaitForCancellation = true };
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ExecuteAsync("local.loopback-listener", probe, cancellation.Token));
    }

    private static async Task<PrerequisiteCheckOutcome> ExecuteAsync(
        string id,
        FakeProbe probe,
        CancellationToken cancellationToken = default)
    {
        var definition = LocalPrerequisiteCatalog.Definitions.Single(
            candidate => candidate.Id == id);
        return await new LocalPrerequisiteCheck(definition, probe)
            .ExecuteAsync(Context(), cancellationToken);
    }

    private static int IndexOf(string id)
    {
        return LocalPrerequisiteCatalog.Definitions
            .Select((definition, index) => (definition, index))
            .Single(item => item.definition.Id == id)
            .index;
    }

    private static PrerequisiteCheckContext Context() =>
        new(
            Guid.NewGuid(),
            "setup",
            new Dictionary<string, string>
            {
                [PrerequisiteSystemInputKeys.CurrentTenantId] = TenantId,
                [PrerequisiteSystemInputKeys.CurrentRoles] = "Operator,Reader",
                [PrerequisiteSystemInputKeys.CurrentClientId] = SpaClientId,
                [PrerequisiteSystemInputKeys.CurrentScopes] =
                    "orchestrator.access_as_user",
            });

    private sealed class FakeProbe : ILocalPrerequisiteProbe
    {
        public LocalHostSnapshot Host { get; init; } = new(true, true);

        public bool ListenerAvailable { get; init; } = true;

        public bool WaitForCancellation { get; init; }

        public LocalCertificateSnapshot Certificate { get; init; } =
            new("CN=localhost", DateTimeOffset.UtcNow.AddDays(90));

        public LocalDatabaseSnapshot Database { get; init; } =
            new("ok", "migration", 0, 2_000_000_000);

        public LocalClockSnapshot Clock { get; init; } =
            new("trusted.example", TimeSpan.Zero);

        public LocalEntraSnapshot Entra { get; init; } =
            new(
                TenantId,
                ApiClientId,
                SpaClientId,
                "https://localhost:9443/auth/callback",
                "orchestrator.access_as_user",
                $"api://{ApiClientId}/orchestrator.access_as_user");

        public LocalHostSnapshot GetHost() => Host;

        public async Task<bool> IsLoopbackListenerAvailableAsync(
            CancellationToken cancellationToken)
        {
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return ListenerAvailable;
        }

        public LocalCertificateSnapshot GetCertificate() => Certificate;

        public Task<LocalDatabaseSnapshot> GetDatabaseAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Database);
        }

        public Task<LocalClockSnapshot> GetClockAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Clock);
        }

        public LocalEntraSnapshot GetEntra() => Entra;
    }
}
