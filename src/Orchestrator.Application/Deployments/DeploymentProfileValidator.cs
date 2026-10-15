using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Fsp.Orchestrator.Application.Deployments;

public sealed partial class DeploymentProfileValidator
    : IDeploymentProfileValidator
{
    public IReadOnlyList<DeploymentProfileValidationError> Validate(
        DeploymentProfile profile)
    {
        var errors = new List<DeploymentProfileValidationError>();
        RequiredName(errors, nameof(profile.Name), profile.Name, 200);
        AzureName(
            errors,
            nameof(profile.ResourceGroupName),
            profile.ResourceGroupName,
            90);
        AzureName(
            errors,
            nameof(profile.DeploymentName),
            profile.DeploymentName,
            64);
        AzureName(errors, nameof(profile.AksName), profile.AksName, 63);
        AzureName(errors, nameof(profile.VnetName), profile.VnetName, 80);
        AzureName(errors, nameof(profile.SubnetName), profile.SubnetName, 80);

        if (profile.SubscriptionId == Guid.Empty)
        {
            Add(
                errors,
                nameof(profile.SubscriptionId),
                "deployment.subscription_invalid",
                "A valid Azure subscription ID is required.");
        }

        if (!LocationPattern().IsMatch(profile.Location))
        {
            Add(
                errors,
                nameof(profile.Location),
                "deployment.location_invalid",
                "Use an Azure region name containing lowercase letters and digits.");
        }

        if (!KubernetesVersionPattern().IsMatch(profile.KubernetesVersion))
        {
            Add(
                errors,
                nameof(profile.KubernetesVersion),
                "deployment.kubernetes_version_invalid",
                "Use a Kubernetes version in major.minor.patch form.");
        }

        if (!VmSkuPattern().IsMatch(profile.NodeVmSku))
        {
            Add(
                errors,
                nameof(profile.NodeVmSku),
                "deployment.sku_invalid",
                "Use an Azure VM SKU beginning with Standard_.");
        }

        if (profile.NodeCount is < 1 or > 100)
        {
            Add(
                errors,
                nameof(profile.NodeCount),
                "deployment.node_count_invalid",
                "Node count must be between 1 and 100.");
        }

        if (profile.RequestedRegionalVcpu < 1)
        {
            Add(
                errors,
                nameof(profile.RequestedRegionalVcpu),
                "deployment.vcpu_invalid",
                "Requested regional vCPU must be positive.");
        }

        ValidateNetworks(profile, errors);
        ResourceId(
            errors,
            nameof(profile.ExistingVnetResourceId),
            profile.ExistingVnetResourceId,
            profile.SubscriptionId,
            "Microsoft.Network/virtualNetworks");
        ResourceId(
            errors,
            nameof(profile.ExistingContainerRegistryResourceId),
            profile.ExistingContainerRegistryResourceId,
            profile.SubscriptionId,
            "Microsoft.ContainerRegistry/registries");
        ResourceId(
            errors,
            nameof(profile.ExistingKeyVaultResourceId),
            profile.ExistingKeyVaultResourceId,
            profile.SubscriptionId,
            "Microsoft.KeyVault/vaults");
        return errors;
    }

    private static void ValidateNetworks(
        DeploymentProfile profile,
        ICollection<DeploymentProfileValidationError> errors)
    {
        var values = new[]
        {
            (nameof(profile.VnetCidr), profile.VnetCidr),
            (nameof(profile.AksSubnetCidr), profile.AksSubnetCidr),
            (nameof(profile.PodCidr), profile.PodCidr),
            (nameof(profile.ServiceCidr), profile.ServiceCidr),
        };
        var parsed = new Dictionary<string, Cidr>(StringComparer.Ordinal);
        foreach (var (field, value) in values)
        {
            if (Cidr.TryParse(value, out var cidr))
            {
                parsed[field] = cidr;
            }
            else
            {
                Add(
                    errors,
                    field,
                    "deployment.cidr_invalid",
                    "Use a canonical IPv4 CIDR with a prefix between 1 and 31.");
            }
        }

        if (parsed.TryGetValue(nameof(profile.VnetCidr), out var vnet)
            && parsed.TryGetValue(nameof(profile.AksSubnetCidr), out var subnet)
            && !vnet.Contains(subnet))
        {
            Add(
                errors,
                nameof(profile.AksSubnetCidr),
                "deployment.subnet_outside_vnet",
                "The AKS subnet must be contained by the virtual network.");
        }

        var isolated = new[]
        {
            nameof(profile.AksSubnetCidr),
            nameof(profile.PodCidr),
            nameof(profile.ServiceCidr),
        };
        for (var left = 0; left < isolated.Length; left++)
        {
            for (var right = left + 1; right < isolated.Length; right++)
            {
                if (parsed.TryGetValue(isolated[left], out var leftCidr)
                    && parsed.TryGetValue(isolated[right], out var rightCidr)
                    && leftCidr.Overlaps(rightCidr))
                {
                    Add(
                        errors,
                        isolated[right],
                        "deployment.cidr_overlap",
                        $"{isolated[left]} and {isolated[right]} must not overlap.");
                }
            }
        }

        if (!IPAddress.TryParse(profile.DnsServiceIp, out var dns)
            || !parsed.TryGetValue(nameof(profile.ServiceCidr), out var service)
            || !service.Contains(dns))
        {
            Add(
                errors,
                nameof(profile.DnsServiceIp),
                "deployment.dns_ip_invalid",
                "The DNS service IP must be inside the service CIDR.");
        }

        var expectedZone =
            $"privatelink.{profile.Location}.azmk8s.io";
        if (!string.Equals(
                profile.PrivateDnsZoneName,
                expectedZone,
                StringComparison.OrdinalIgnoreCase))
        {
            Add(
                errors,
                nameof(profile.PrivateDnsZoneName),
                "deployment.private_dns_invalid",
                $"Use the private AKS DNS zone '{expectedZone}'.");
        }
    }

    private static void RequiredName(
        ICollection<DeploymentProfileValidationError> errors,
        string field,
        string value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            Add(
                errors,
                field,
                "deployment.name_invalid",
                $"The value is required and must not exceed {maximumLength} characters.");
        }
    }

    private static void AzureName(
        ICollection<DeploymentProfileValidationError> errors,
        string field,
        string value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.Any(
                character => !(char.IsLetterOrDigit(character)
                    || character is '-' or '_' or '.' or '(' or ')'))
            || value.EndsWith('.'))
        {
            Add(
                errors,
                field,
                "deployment.azure_name_invalid",
                $"Use a valid Azure name no longer than {maximumLength} characters.");
        }
    }

    private static void ResourceId(
        ICollection<DeploymentProfileValidationError> errors,
        string field,
        string? value,
        Guid subscriptionId,
        string expectedType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var pattern =
            $"^/subscriptions/{Regex.Escape(subscriptionId.ToString("D"))}"
            + "/resourceGroups/[^/]+/providers/"
            + Regex.Escape(expectedType)
            + "/[^/]+$";
        if (!Regex.IsMatch(
                value,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)))
        {
            Add(
                errors,
                field,
                "deployment.resource_id_invalid",
                $"Use a complete {expectedType} resource ID in the selected subscription.");
        }
    }

    private static void Add(
        ICollection<DeploymentProfileValidationError> errors,
        string field,
        string code,
        string message) =>
        errors.Add(new DeploymentProfileValidationError(field, code, message));

    [GeneratedRegex("^[a-z][a-z0-9]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex LocationPattern();

    [GeneratedRegex(
        "^[0-9]+\\.[0-9]+\\.[0-9]+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex KubernetesVersionPattern();

    [GeneratedRegex(
        "^Standard_[A-Za-z0-9_\\-]+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VmSkuPattern();

    private readonly record struct Cidr(uint Network, int Prefix)
    {
        public static bool TryParse(string value, out Cidr cidr)
        {
            cidr = default;
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
                return false;
            }

            var bytes = address.GetAddressBytes();
            var raw = ((uint)bytes[0] << 24)
                | ((uint)bytes[1] << 16)
                | ((uint)bytes[2] << 8)
                | bytes[3];
            var mask = Mask(prefix);
            if ((raw & mask) != raw)
            {
                return false;
            }

            cidr = new Cidr(raw, prefix);
            return true;
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
