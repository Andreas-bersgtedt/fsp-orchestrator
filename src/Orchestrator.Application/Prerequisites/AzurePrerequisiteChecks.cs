using System.Globalization;
using System.Net;

namespace Fsp.Orchestrator.Application.Prerequisites;

public interface IAzurePrerequisiteProbe
{
    bool HasSession(string tenantId, string objectId);

    Task<IReadOnlyList<string>> GetUnregisteredProvidersAsync(
        string tenantId,
        string objectId,
        Guid subscriptionId,
        IReadOnlyList<string> providers,
        CancellationToken cancellationToken);

    Task<AzurePermissionSnapshot> CheckPermissionsAsync(
        string tenantId,
        string objectId,
        Guid subscriptionId,
        string resourceGroup,
        IReadOnlyList<string> requiredActions,
        CancellationToken cancellationToken);

    Task<AzureQuotaSnapshot> GetRegionalVcpuQuotaAsync(
        string tenantId,
        string objectId,
        Guid subscriptionId,
        string location,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetKubernetesVersionsAsync(
        string tenantId,
        string objectId,
        Guid subscriptionId,
        string location,
        CancellationToken cancellationToken);
}

public sealed record AzurePermissionSnapshot(
    string Scope,
    IReadOnlyList<string> DeniedActions);

public sealed record AzureQuotaSnapshot(
    long CurrentValue,
    long Limit);

public sealed class AzurePrerequisiteProbeException(
    string code,
    string safeMessage)
    : Exception(safeMessage)
{
    public string Code { get; } = code;
}

public static class AzurePrerequisiteInputKeys
{
    public const string SubscriptionId = "subscriptionId";
    public const string ResourceGroupName = "resourceGroupName";
    public const string Location = "location";
    public const string KubernetesVersion = "kubernetesVersion";
    public const string RequestedRegionalVcpu = "requestedRegionalVcpu";
    public const string AksName = "aksName";
    public const string VnetName = "vnetName";
    public const string SubnetName = "subnetName";
    public const string VnetCidr = "vnetCidr";
    public const string AksSubnetCidr = "aksSubnetCidr";
    public const string PodCidr = "podCidr";
    public const string ServiceCidr = "serviceCidr";
    public const string DnsServiceIp = "dnsServiceIp";
    public const string PrivateDnsZoneName = "privateDnsZoneName";
}

public static class AzurePrerequisiteCatalog
{
    public static IReadOnlyList<PrerequisiteCheckDefinition> Definitions { get; } =
    [
        Definition(
            "azure.session",
            "Delegated Azure session",
            "Checks that the signed-in operator has an active ARM session.",
            ["entra.tenant"],
            [
                PrerequisiteSystemInputKeys.CurrentTenantId,
                PrerequisiteSystemInputKeys.CurrentObjectId,
            ]),
        Definition(
            "azure.providers",
            "Azure provider registration",
            "Checks required Azure resource provider registration.",
            ["azure.session"],
            [AzurePrerequisiteInputKeys.SubscriptionId]),
        Definition(
            "azure.permissions",
            "Azure deployment permissions",
            "Checks exact deployment actions at the selected resource-group scope.",
            ["azure.providers"],
            [
                AzurePrerequisiteInputKeys.SubscriptionId,
                AzurePrerequisiteInputKeys.ResourceGroupName,
            ]),
        Definition(
            "azure.quota",
            "Regional vCPU quota",
            "Checks current regional vCPU use, limit, and requested amount.",
            ["azure.permissions"],
            [
                AzurePrerequisiteInputKeys.SubscriptionId,
                AzurePrerequisiteInputKeys.Location,
                AzurePrerequisiteInputKeys.RequestedRegionalVcpu,
            ],
            applyFreshness: TimeSpan.FromSeconds(30)),
        Definition(
            "azure.region",
            "AKS regional availability",
            "Checks the selected Kubernetes version in the target region.",
            ["azure.providers"],
            [
                AzurePrerequisiteInputKeys.SubscriptionId,
                AzurePrerequisiteInputKeys.Location,
                AzurePrerequisiteInputKeys.KubernetesVersion,
            ],
            applyFreshness: TimeSpan.FromSeconds(30)),
        Definition(
            "azure.naming",
            "Azure resource names",
            "Validates resource-group, AKS, virtual-network, and subnet names.",
            [],
            [
                AzurePrerequisiteInputKeys.ResourceGroupName,
                AzurePrerequisiteInputKeys.AksName,
                AzurePrerequisiteInputKeys.VnetName,
                AzurePrerequisiteInputKeys.SubnetName,
            ]),
        Definition(
            "azure.network",
            "AKS network ranges and private DNS",
            "Checks subnet containment, CIDR overlap, DNS service IP, and private DNS.",
            [],
            [
                AzurePrerequisiteInputKeys.Location,
                AzurePrerequisiteInputKeys.VnetCidr,
                AzurePrerequisiteInputKeys.AksSubnetCidr,
                AzurePrerequisiteInputKeys.PodCidr,
                AzurePrerequisiteInputKeys.ServiceCidr,
                AzurePrerequisiteInputKeys.DnsServiceIp,
                AzurePrerequisiteInputKeys.PrivateDnsZoneName,
            ]),
    ];

    private static PrerequisiteCheckDefinition Definition(
        string id,
        string displayName,
        string description,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<string> inputKeys,
        TimeSpan? applyFreshness = null) =>
        new(
            id,
            "Azure deployment",
            displayName,
            description,
            dependencies,
            ["deploy"],
            inputKeys,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMinutes(5),
            true,
            "1.0",
            applyFreshness);
}

public sealed class AzurePrerequisiteCheck(
    PrerequisiteCheckDefinition definition,
    IAzurePrerequisiteProbe probe)
    : IPrerequisiteCheck
{
    private static readonly string[] Providers =
    [
        "Microsoft.ContainerService",
        "Microsoft.Compute",
        "Microsoft.KeyVault",
        "Microsoft.ManagedIdentity",
        "Microsoft.Network",
    ];
    private static readonly string[] RequiredActions =
    [
        "Microsoft.Authorization/roleAssignments/write",
        "Microsoft.ContainerService/managedClusters/read",
        "Microsoft.ContainerService/managedClusters/runCommand/action",
        "Microsoft.ContainerService/managedClusters/write",
        "Microsoft.ManagedIdentity/userAssignedIdentities/assign/action",
        "Microsoft.Network/virtualNetworks/subnets/join/action",
        "Microsoft.Resources/deployments/validate/action",
        "Microsoft.Resources/deployments/write",
    ];
    private static readonly IReadOnlyDictionary<string, string> EmptyEvidence =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public PrerequisiteCheckDefinition Definition { get; } = definition;

    public async Task<PrerequisiteCheckOutcome> ExecuteAsync(
        PrerequisiteCheckContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return Definition.Id switch
            {
                "azure.session" => Session(context),
                "azure.providers" => await ProvidersAsync(context, cancellationToken),
                "azure.permissions" => await PermissionsAsync(
                    context,
                    cancellationToken),
                "azure.quota" => await QuotaAsync(context, cancellationToken),
                "azure.region" => await RegionAsync(context, cancellationToken),
                "azure.naming" => Naming(context),
                "azure.network" => Network(context),
                _ => throw new InvalidOperationException(
                    $"Unsupported Azure prerequisite check '{Definition.Id}'."),
            };
        }
        catch (AzurePrerequisiteProbeException exception)
        {
            return new PrerequisiteCheckOutcome(
                PrerequisiteCheckStatus.Unknown,
                exception.Code,
                exception.Message,
                EmptyEvidence,
                "Reconnect Azure and rerun this check.");
        }
    }

    private PrerequisiteCheckOutcome Session(PrerequisiteCheckContext context)
    {
        var tenantId = Required(
            context,
            PrerequisiteSystemInputKeys.CurrentTenantId);
        var objectId = Required(
            context,
            PrerequisiteSystemInputKeys.CurrentObjectId);
        return probe.HasSession(tenantId, objectId)
            ? Passed(
                "azure.session.passed",
                "The delegated ARM session is active.")
            : Failed(
                "azure.sign_in_required",
                "A delegated ARM session is required.",
                "Connect Azure with the DeploymentAdmin account.");
    }

    private async Task<PrerequisiteCheckOutcome> ProvidersAsync(
        PrerequisiteCheckContext context,
        CancellationToken cancellationToken)
    {
        var subscriptionId = GuidInput(
            context,
            AzurePrerequisiteInputKeys.SubscriptionId);
        var identity = Identity(context);
        var unregistered = await probe.GetUnregisteredProvidersAsync(
            identity.TenantId,
            identity.ObjectId,
            subscriptionId,
            Providers,
            cancellationToken);
        return unregistered.Count == 0
            ? Passed(
                "azure.providers.passed",
                "Required Azure resource providers are registered.")
            : Failed(
                "azure.providers.unregistered",
                "One or more Azure resource providers are not registered.",
                "Register the listed providers and rerun the check.",
                new Dictionary<string, string>
                {
                    ["providers"] = string.Join(",", unregistered),
                });
    }

    private async Task<PrerequisiteCheckOutcome> PermissionsAsync(
        PrerequisiteCheckContext context,
        CancellationToken cancellationToken)
    {
        var subscriptionId = GuidInput(
            context,
            AzurePrerequisiteInputKeys.SubscriptionId);
        var resourceGroup = Required(
            context,
            AzurePrerequisiteInputKeys.ResourceGroupName);
        var identity = Identity(context);
        var result = await probe.CheckPermissionsAsync(
            identity.TenantId,
            identity.ObjectId,
            subscriptionId,
            resourceGroup,
            RequiredActions,
            cancellationToken);
        if (result.DeniedActions.Count == 0)
        {
            return Passed(
                "azure.permissions.passed",
                "Required Azure actions are allowed.",
                new Dictionary<string, string> { ["scope"] = result.Scope });
        }

        return Failed(
            "azure.permission_denied",
            $"Azure action '{result.DeniedActions[0]}' is denied at scope '{result.Scope}'.",
            "Grant the denied action at the selected resource-group scope.",
            new Dictionary<string, string>
            {
                ["deniedAction"] = result.DeniedActions[0],
                ["scope"] = result.Scope,
            });
    }

    private async Task<PrerequisiteCheckOutcome> QuotaAsync(
        PrerequisiteCheckContext context,
        CancellationToken cancellationToken)
    {
        var subscriptionId = GuidInput(
            context,
            AzurePrerequisiteInputKeys.SubscriptionId);
        var location = Required(
            context,
            AzurePrerequisiteInputKeys.Location);
        var requested = LongInput(
            context,
            AzurePrerequisiteInputKeys.RequestedRegionalVcpu);
        var identity = Identity(context);
        var quota = await probe.GetRegionalVcpuQuotaAsync(
            identity.TenantId,
            identity.ObjectId,
            subscriptionId,
            location,
            cancellationToken);
        var evidence = new Dictionary<string, string>
        {
            ["current"] = quota.CurrentValue.ToString(CultureInfo.InvariantCulture),
            ["limit"] = quota.Limit.ToString(CultureInfo.InvariantCulture),
            ["requested"] = requested.ToString(CultureInfo.InvariantCulture),
        };
        return quota.CurrentValue + requested <= quota.Limit
            ? Passed(
                "azure.quota.passed",
                "Regional vCPU quota is sufficient.",
                evidence)
            : Failed(
                "azure.quota.insufficient",
                "Regional vCPU quota is insufficient.",
                "Reduce requested capacity or obtain a quota increase.",
                evidence);
    }

    private async Task<PrerequisiteCheckOutcome> RegionAsync(
        PrerequisiteCheckContext context,
        CancellationToken cancellationToken)
    {
        var subscriptionId = GuidInput(
            context,
            AzurePrerequisiteInputKeys.SubscriptionId);
        var location = Required(
            context,
            AzurePrerequisiteInputKeys.Location);
        var version = Required(
            context,
            AzurePrerequisiteInputKeys.KubernetesVersion);
        var identity = Identity(context);
        var versions = await probe.GetKubernetesVersionsAsync(
            identity.TenantId,
            identity.ObjectId,
            subscriptionId,
            location,
            cancellationToken);
        return versions.Contains(version, StringComparer.Ordinal)
            ? Passed(
                "azure.region.passed",
                "The Kubernetes version is available in the selected region.",
                new Dictionary<string, string>
                {
                    ["kubernetesVersion"] = version,
                    ["location"] = location,
                })
            : Failed(
                "azure.kubernetes_version_unavailable",
                "The Kubernetes version is unavailable in the selected region.",
                "Select an available Kubernetes version or region.",
                new Dictionary<string, string>
                {
                    ["kubernetesVersion"] = version,
                    ["location"] = location,
                });
    }

    private static PrerequisiteCheckOutcome Naming(
        PrerequisiteCheckContext context)
    {
        var inputs = new[]
        {
            (AzurePrerequisiteInputKeys.ResourceGroupName, 90),
            (AzurePrerequisiteInputKeys.AksName, 63),
            (AzurePrerequisiteInputKeys.VnetName, 80),
            (AzurePrerequisiteInputKeys.SubnetName, 80),
        };
        foreach (var (key, maximumLength) in inputs)
        {
            var value = Required(context, key);
            if (!ValidName(value, maximumLength))
            {
                return Failed(
                    "azure.name_invalid",
                    $"Azure name '{key}' is invalid.",
                    "Use letters, numbers, hyphens, underscores, or periods within the Azure length limit.",
                    new Dictionary<string, string> { ["field"] = key });
            }
        }

        return Passed(
            "azure.naming.passed",
            "Azure resource names are valid.");
    }

    private static PrerequisiteCheckOutcome Network(
        PrerequisiteCheckContext context)
    {
        var vnet = Cidr.Parse(Required(
            context,
            AzurePrerequisiteInputKeys.VnetCidr));
        var subnet = Cidr.Parse(Required(
            context,
            AzurePrerequisiteInputKeys.AksSubnetCidr));
        var pod = Cidr.Parse(Required(
            context,
            AzurePrerequisiteInputKeys.PodCidr));
        var service = Cidr.Parse(Required(
            context,
            AzurePrerequisiteInputKeys.ServiceCidr));
        var dns = IPAddress.Parse(Required(
            context,
            AzurePrerequisiteInputKeys.DnsServiceIp));
        var location = Required(
            context,
            AzurePrerequisiteInputKeys.Location);
        var privateDns = Required(
            context,
            AzurePrerequisiteInputKeys.PrivateDnsZoneName);
        if (!vnet.Contains(subnet))
        {
            return Failed(
                "azure.subnet_outside_vnet",
                "The AKS subnet is outside the virtual-network range.",
                "Choose an AKS subnet contained by the virtual-network CIDR.");
        }

        var ranges = new[]
        {
            (Name: "aksSubnetCidr", Value: subnet),
            (Name: "podCidr", Value: pod),
            (Name: "serviceCidr", Value: service),
        };
        for (var left = 0; left < ranges.Length; left++)
        {
            for (var right = left + 1; right < ranges.Length; right++)
            {
                if (ranges[left].Value.Overlaps(ranges[right].Value))
                {
                    return Failed(
                        "azure.network_overlap",
                        $"Network ranges '{ranges[left].Name}' and '{ranges[right].Name}' overlap.",
                        "Choose non-overlapping AKS subnet, pod, and service CIDRs.",
                        new Dictionary<string, string>
                        {
                            ["left"] = ranges[left].Name,
                            ["right"] = ranges[right].Name,
                        });
                }
            }
        }

        if (!service.Contains(dns))
        {
            return Failed(
                "azure.dns_service_ip_outside_range",
                "The DNS service IP is outside the service CIDR.",
                "Choose a DNS service IP inside the service CIDR.");
        }

        var expectedDns = $"privatelink.{location}.azmk8s.io";
        if (!string.Equals(
                privateDns,
                expectedDns,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                "azure.private_dns_invalid",
                "The private DNS zone does not match the AKS region.",
                $"Use private DNS zone '{expectedDns}'.",
                new Dictionary<string, string>
                {
                    ["expected"] = expectedDns,
                    ["observed"] = privateDns,
                });
        }

        return Passed(
            "azure.network.passed",
            "AKS network ranges and private DNS are valid.");
    }

    private static bool ValidName(string value, int maximumLength) =>
        value.Length <= maximumLength
        && char.IsLetterOrDigit(value[0])
        && char.IsLetterOrDigit(value[^1])
        && value.All(
            character => char.IsLetterOrDigit(character)
                || character is '-' or '_' or '.');

    private static Guid GuidInput(
        PrerequisiteCheckContext context,
        string key) =>
        Guid.TryParse(Required(context, key), out var value)
            ? value
            : throw new AzurePrerequisiteProbeException(
                "azure.input_invalid",
                $"Azure input '{key}' must be a GUID.");

    private static (string TenantId, string ObjectId) Identity(
        PrerequisiteCheckContext context) =>
        (
            Required(context, PrerequisiteSystemInputKeys.CurrentTenantId),
            Required(context, PrerequisiteSystemInputKeys.CurrentObjectId)
        );

    private static long LongInput(
        PrerequisiteCheckContext context,
        string key) =>
        long.TryParse(
            Required(context, key),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
            && value > 0
                ? value
                : throw new AzurePrerequisiteProbeException(
                    "azure.input_invalid",
                    $"Azure input '{key}' must be a positive integer.");

    private static string Required(
        PrerequisiteCheckContext context,
        string key) =>
        context.Inputs.GetValueOrDefault(key)?.Trim() is { Length: > 0 } value
            ? value
            : throw new AzurePrerequisiteProbeException(
                "azure.input_missing",
                $"Azure input '{key}' is required.");

    private static PrerequisiteCheckOutcome Passed(
        string code,
        string summary,
        IReadOnlyDictionary<string, string>? evidence = null) =>
        new(
            PrerequisiteCheckStatus.Passed,
            code,
            summary,
            evidence ?? EmptyEvidence,
            string.Empty);

    private static PrerequisiteCheckOutcome Failed(
        string code,
        string summary,
        string remediation,
        IReadOnlyDictionary<string, string>? evidence = null) =>
        new(
            PrerequisiteCheckStatus.Failed,
            code,
            summary,
            evidence ?? EmptyEvidence,
            remediation);

    private readonly record struct Cidr(uint Network, int Prefix)
    {
        public static Cidr Parse(string value)
        {
            var parts = value.Split('/');
            if (parts.Length != 2
                || !IPAddress.TryParse(parts[0], out var address)
                || address.AddressFamily
                    != System.Net.Sockets.AddressFamily.InterNetwork
                || !int.TryParse(
                    parts[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var prefix)
                || prefix is < 1 or > 31)
            {
                throw new AzurePrerequisiteProbeException(
                    "azure.cidr_invalid",
                    $"Network range '{value}' is not a valid IPv4 CIDR.");
            }

            var bytes = address.GetAddressBytes();
            var raw = ((uint)bytes[0] << 24)
                | ((uint)bytes[1] << 16)
                | ((uint)bytes[2] << 8)
                | bytes[3];
            var mask = uint.MaxValue << (32 - prefix);
            return new Cidr(raw & mask, prefix);
        }

        public bool Contains(Cidr other) =>
            Prefix <= other.Prefix
            && Network == (other.Network & Mask(Prefix));

        public bool Contains(IPAddress address)
        {
            if (address.AddressFamily
                != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return false;
            }

            var bytes = address.GetAddressBytes();
            var raw = ((uint)bytes[0] << 24)
                | ((uint)bytes[1] << 16)
                | ((uint)bytes[2] << 8)
                | bytes[3];
            return Network == (raw & Mask(Prefix));
        }

        public bool Overlaps(Cidr other)
        {
            var commonPrefix = Math.Min(Prefix, other.Prefix);
            return (Network & Mask(commonPrefix))
                == (other.Network & Mask(commonPrefix));
        }

        private static uint Mask(int prefix) =>
            uint.MaxValue << (32 - prefix);
    }
}
