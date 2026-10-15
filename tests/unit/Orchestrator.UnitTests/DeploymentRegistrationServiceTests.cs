using Fsp.Orchestrator.Application.Deployments;
using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.UnitTests;

public sealed class DeploymentRegistrationServiceTests
{
    [Fact]
    public async Task SuccessfulProbeIsPersistedAtomicallyWithCredentialReference()
    {
        var store = new TestInventoryStore();
        var probe = new TestProbe(
            new DeploymentProbeResult(
                FspTopology.Enterprise,
                [new ObservedComponent("manager", "3.1.0", "healthy")]));
        var service = new DeploymentRegistrationService(
            store,
            probe,
            TimeProvider.System);
        var credentialReference = new Uri(
            "https://example.vault.azure.net/secrets/fsp-manager");

        var result = await service.RegisterAsync(
            Request(
                new Uri("https://fsp.example.test/"),
                FspCredentialKind.Bearer,
                credentialReference),
            CancellationToken.None);

        Assert.Equal("Enterprise", result.Deployment.Topology);
        Assert.Equal(credentialReference, result.Deployment.CredentialReference);
        Assert.Equal("Bearer", result.Deployment.CredentialKind);
        Assert.Equal("3.1.0", Assert.Single(result.Components).Version);
        Assert.NotNull(store.Registered);
        Assert.Equal(credentialReference, store.Registered.CredentialReference);
    }

    [Fact]
    public async Task FailedProbeLeavesNoInventoryRecord()
    {
        var store = new TestInventoryStore();
        var probe = new TestProbe(
            new DeploymentRegistrationException(
                DeploymentRegistrationFailure.ContractMismatch,
                "deployment.contract_mismatch",
                "Contract mismatch."));
        var service = new DeploymentRegistrationService(
            store,
            probe,
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<DeploymentRegistrationException>(
            () => service.RegisterAsync(
                Request(new Uri("https://fsp.example.test")),
                CancellationToken.None));

        Assert.Equal("deployment.contract_mismatch", exception.Code);
        Assert.Null(store.Registered);
    }

    [Fact]
    public async Task DuplicateEndpointIsRejectedBeforeProbe()
    {
        var store = new TestInventoryStore { EndpointExists = true };
        var probe = new TestProbe(
            new DeploymentProbeResult(
                FspTopology.Lite,
                [new ObservedComponent("lite", "3.1.0", "healthy")]));
        var service = new DeploymentRegistrationService(
            store,
            probe,
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<DeploymentRegistrationException>(
            () => service.RegisterAsync(
                Request(new Uri("https://fsp.example.test")),
                CancellationToken.None));

        Assert.Equal("deployment.endpoint_duplicate", exception.Code);
        Assert.False(probe.Executed);
        Assert.Null(store.Registered);
    }

    [Theory]
    [InlineData("http://fsp.example.test", FspCredentialKind.None, null)]
    [InlineData(
        "https://fsp.example.test",
        FspCredentialKind.Bearer,
        "https://example.test/secrets/token")]
    public async Task InvalidEndpointOrCredentialReferenceIsRejected(
        string endpoint,
        FspCredentialKind kind,
        string? reference)
    {
        var service = new DeploymentRegistrationService(
            new TestInventoryStore(),
            new TestProbe(
                new DeploymentProbeResult(
                    FspTopology.Lite,
                    [new ObservedComponent("lite", "3.1.0", "healthy")])),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<DeploymentRegistrationException>(
            () => service.RegisterAsync(
                Request(
                    new Uri(endpoint),
                    kind,
                    reference is null ? null : new Uri(reference)),
                CancellationToken.None));

        Assert.Equal(
            DeploymentRegistrationFailure.InvalidInput,
            exception.Failure);
    }

    private static DeploymentRegistrationRequest Request(
        Uri endpoint,
        FspCredentialKind kind = FspCredentialKind.None,
        Uri? credentialReference = null) =>
        new(
            Guid.NewGuid(),
            "Test deployment",
            endpoint,
            null,
            kind,
            credentialReference);

    private sealed class TestProbe : IFspDeploymentProbe
    {
        private readonly DeploymentProbeResult? result;
        private readonly DeploymentRegistrationException? exception;

        public TestProbe(DeploymentProbeResult result)
        {
            this.result = result;
        }

        public TestProbe(DeploymentRegistrationException exception)
        {
            this.exception = exception;
        }

        public bool Executed { get; private set; }

        public Task<DeploymentProbeResult> ProbeAsync(
            DeploymentProbeRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Executed = true;
            return exception is null
                ? Task.FromResult(result!)
                : Task.FromException<DeploymentProbeResult>(exception);
        }
    }

    private sealed class TestInventoryStore : IInventoryStore
    {
        public bool EndpointExists { get; init; }

        public RegisterDeployment? Registered { get; private set; }

        public Task<IReadOnlyList<DeploymentInventoryItem>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeploymentInventoryItem>>([]);

        public Task<DeploymentInventoryItem?> GetAsync(
            Guid deploymentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<DeploymentInventoryItem?>(null);

        public Task<bool> ExistsByManagerEndpointAsync(
            Uri managerEndpoint,
            CancellationToken cancellationToken) =>
            Task.FromResult(EndpointExists);

        public Task<DeploymentInventoryItem> RegisterAsync(
            RegisterDeployment request,
            CancellationToken cancellationToken)
        {
            Registered = request;
            return Task.FromResult(
                new DeploymentInventoryItem(
                    request.Id,
                    request.Name,
                    request.Topology,
                    request.ManagerEndpoint,
                    request.AzureResourceId,
                    request.CredentialKind,
                    request.CredentialReference,
                    request.ObservedAt,
                    request.ObservedAt,
                    1));
        }

        public Task<DeploymentInventoryItem> UpsertAsync(
            UpsertDeployment request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeploymentObservation> RecordObservationAsync(
            RecordObservation request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

    }
}
