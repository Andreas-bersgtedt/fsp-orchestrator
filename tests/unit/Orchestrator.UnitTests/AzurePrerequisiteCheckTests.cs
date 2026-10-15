using Fsp.Orchestrator.Application.Prerequisites;

namespace Fsp.Orchestrator.UnitTests;

public sealed class AzurePrerequisiteCheckTests
{
    [Fact]
    public async Task PermissionFailureNamesDeniedActionAndScope()
    {
        var probe = new TestProbe
        {
            Permission = new AzurePermissionSnapshot(
                "/subscriptions/sub/resourceGroups/fsp",
                ["Microsoft.Resources/deployments/write"]),
        };

        var outcome = await ExecuteAsync(
            "azure.permissions",
            probe,
            Inputs());

        Assert.Equal(PrerequisiteCheckStatus.Failed, outcome.Status);
        Assert.Equal("azure.permission_denied", outcome.Code);
        Assert.Equal(
            "Microsoft.Resources/deployments/write",
            outcome.Evidence["deniedAction"]);
        Assert.Equal(
            "/subscriptions/sub/resourceGroups/fsp",
            outcome.Evidence["scope"]);
    }

    [Fact]
    public async Task QuotaReportsCurrentLimitAndRequested()
    {
        var probe = new TestProbe
        {
            Quota = new AzureQuotaSnapshot(12, 16),
        };

        var outcome = await ExecuteAsync(
            "azure.quota",
            probe,
            Inputs());

        Assert.Equal(PrerequisiteCheckStatus.Passed, outcome.Status);
        Assert.Equal("12", outcome.Evidence["current"]);
        Assert.Equal("16", outcome.Evidence["limit"]);
        Assert.Equal("4", outcome.Evidence["requested"]);
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            Definition("azure.quota").ApplyFreshness);
    }

    [Fact]
    public async Task OverlappingNetworkRangesFailWithBothFields()
    {
        var inputs = Inputs();
        inputs[AzurePrerequisiteInputKeys.PodCidr] = "10.0.0.0/24";
        inputs[AzurePrerequisiteInputKeys.AksSubnetCidr] = "10.0.0.0/24";

        var outcome = await ExecuteAsync(
            "azure.network",
            new TestProbe(),
            inputs);

        Assert.Equal("azure.network_overlap", outcome.Code);
        Assert.Equal("aksSubnetCidr", outcome.Evidence["left"]);
        Assert.Equal("podCidr", outcome.Evidence["right"]);
    }

    [Fact]
    public async Task UnknownProviderResponseBlocksTheCheck()
    {
        var outcome = await ExecuteAsync(
            "azure.providers",
            new TestProbe
            {
                Exception = new AzurePrerequisiteProbeException(
                    "azure.arm_response_invalid",
                    "ARM response invalid."),
            },
            Inputs());

        Assert.Equal(PrerequisiteCheckStatus.Unknown, outcome.Status);
        Assert.Equal("azure.arm_response_invalid", outcome.Code);
        Assert.True(Definition("azure.providers").Blocking);
    }

    [Fact]
    public async Task MissingAzureSessionFailsBeforeArmChecks()
    {
        var outcome = await ExecuteAsync(
            "azure.session",
            new TestProbe { HasActiveSession = false },
            Inputs());

        Assert.Equal(PrerequisiteCheckStatus.Failed, outcome.Status);
        Assert.Equal("azure.sign_in_required", outcome.Code);
    }

    private static async Task<PrerequisiteCheckOutcome> ExecuteAsync(
        string id,
        TestProbe probe,
        IReadOnlyDictionary<string, string> inputs) =>
        await new AzurePrerequisiteCheck(Definition(id), probe)
            .ExecuteAsync(
                new PrerequisiteCheckContext(
                    Guid.NewGuid(),
                    "deploy",
                    inputs),
                CancellationToken.None);

    private static PrerequisiteCheckDefinition Definition(string id) =>
        AzurePrerequisiteCatalog.Definitions.Single(
            definition => definition.Id == id);

    private static Dictionary<string, string> Inputs() =>
        new(StringComparer.Ordinal)
        {
            [PrerequisiteSystemInputKeys.CurrentTenantId] =
                "00000000-0000-0000-0000-000000000001",
            [PrerequisiteSystemInputKeys.CurrentObjectId] =
                "00000000-0000-0000-0000-000000000002",
            [AzurePrerequisiteInputKeys.SubscriptionId] =
                "00000000-0000-0000-0000-000000000003",
            [AzurePrerequisiteInputKeys.ResourceGroupName] = "fsp-rg",
            [AzurePrerequisiteInputKeys.Location] = "swedencentral",
            [AzurePrerequisiteInputKeys.KubernetesVersion] = "1.32.7",
            [AzurePrerequisiteInputKeys.RequestedRegionalVcpu] = "4",
            [AzurePrerequisiteInputKeys.AksName] = "fsp-aks",
            [AzurePrerequisiteInputKeys.VnetName] = "fsp-vnet",
            [AzurePrerequisiteInputKeys.SubnetName] = "aks-subnet",
            [AzurePrerequisiteInputKeys.VnetCidr] = "10.0.0.0/16",
            [AzurePrerequisiteInputKeys.AksSubnetCidr] = "10.0.0.0/24",
            [AzurePrerequisiteInputKeys.PodCidr] = "10.1.0.0/16",
            [AzurePrerequisiteInputKeys.ServiceCidr] = "10.2.0.0/16",
            [AzurePrerequisiteInputKeys.DnsServiceIp] = "10.2.0.10",
            [AzurePrerequisiteInputKeys.PrivateDnsZoneName] =
                "privatelink.swedencentral.azmk8s.io",
        };

    private sealed class TestProbe : IAzurePrerequisiteProbe
    {
        public bool HasActiveSession { get; init; } = true;

        public AzurePermissionSnapshot Permission { get; init; } =
            new("/subscriptions/sub/resourceGroups/fsp", []);

        public AzureQuotaSnapshot Quota { get; init; } = new(0, 100);

        public AzurePrerequisiteProbeException? Exception { get; init; }

        public bool HasSession(string tenantId, string objectId) =>
            HasActiveSession;

        public Task<IReadOnlyList<string>> GetUnregisteredProvidersAsync(
            string tenantId,
            string objectId,
            Guid subscriptionId,
            IReadOnlyList<string> providers,
            CancellationToken cancellationToken) =>
            Result<IReadOnlyList<string>>([]);

        public Task<AzurePermissionSnapshot> CheckPermissionsAsync(
            string tenantId,
            string objectId,
            Guid subscriptionId,
            string resourceGroup,
            IReadOnlyList<string> requiredActions,
            CancellationToken cancellationToken) =>
            Result(Permission);

        public Task<AzureQuotaSnapshot> GetRegionalVcpuQuotaAsync(
            string tenantId,
            string objectId,
            Guid subscriptionId,
            string location,
            CancellationToken cancellationToken) =>
            Result(Quota);

        public Task<IReadOnlyList<string>> GetKubernetesVersionsAsync(
            string tenantId,
            string objectId,
            Guid subscriptionId,
            string location,
            CancellationToken cancellationToken) =>
            Result<IReadOnlyList<string>>(["1.32.7"]);

        private Task<T> Result<T>(T value) =>
            Exception is null
                ? Task.FromResult(value)
                : Task.FromException<T>(Exception);
    }
}
