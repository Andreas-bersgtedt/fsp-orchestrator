using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Fsp.Orchestrator.Application.Deployments;

public sealed record DeploymentProfile(
    Guid Id,
    string Name,
    Guid SubscriptionId,
    string ResourceGroupName,
    string DeploymentName,
    string Location,
    string KubernetesVersion,
    string NodeVmSku,
    int NodeCount,
    int RequestedRegionalVcpu,
    string AksName,
    string VnetName,
    string SubnetName,
    string VnetCidr,
    string AksSubnetCidr,
    string PodCidr,
    string ServiceCidr,
    string DnsServiceIp,
    string PrivateDnsZoneName,
    string? ExistingVnetResourceId,
    string? ExistingContainerRegistryResourceId,
    string? ExistingKeyVaultResourceId)
{
    public IReadOnlyDictionary<string, string> ToPrerequisiteInputs() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = Name,
            ["subscriptionId"] = SubscriptionId.ToString("D"),
            ["resourceGroupName"] = ResourceGroupName,
            ["deploymentName"] = DeploymentName,
            ["location"] = Location,
            ["kubernetesVersion"] = KubernetesVersion,
            ["nodeVmSku"] = NodeVmSku,
            ["nodeCount"] = NodeCount.ToString(CultureInfo.InvariantCulture),
            ["requestedRegionalVcpu"] =
                RequestedRegionalVcpu.ToString(CultureInfo.InvariantCulture),
            ["aksName"] = AksName,
            ["vnetName"] = VnetName,
            ["subnetName"] = SubnetName,
            ["vnetCidr"] = VnetCidr,
            ["aksSubnetCidr"] = AksSubnetCidr,
            ["podCidr"] = PodCidr,
            ["serviceCidr"] = ServiceCidr,
            ["dnsServiceIp"] = DnsServiceIp,
            ["privateDnsZoneName"] = PrivateDnsZoneName,
            ["existingVnetResourceId"] = ExistingVnetResourceId ?? string.Empty,
            ["existingContainerRegistryResourceId"] =
                ExistingContainerRegistryResourceId ?? string.Empty,
            ["existingKeyVaultResourceId"] =
                ExistingKeyVaultResourceId ?? string.Empty,
        };
}

public sealed record DeploymentProfileDocument(
    DeploymentProfile Profile,
    string Fingerprint,
    long Revision,
    Guid? PrerequisiteRunId,
    Guid? CurrentPreviewId,
    Guid? CurrentApprovalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DeploymentProfileValidationError(
    string Field,
    string Code,
    string Message);

public sealed record DeploymentActor(
    string TenantId,
    string ObjectId);

public sealed record ArmDeploymentPreview(
    string ValidationCorrelationId,
    string WhatIfCorrelationId,
    IReadOnlyList<DeploymentWhatIfChange> Changes);

public sealed record DeploymentWhatIfChange(
    string ResourceId,
    DeploymentChangeType ChangeType,
    string? BeforeJson,
    string? AfterJson,
    string? DeltaJson);

public sealed record DeploymentPreview(
    Guid Id,
    Guid ProfileId,
    string ProfileFingerprint,
    Guid PrerequisiteRunId,
    string ValidationCorrelationId,
    string WhatIfCorrelationId,
    int CreateCount,
    int ModifyCount,
    int DeleteCount,
    IReadOnlyList<DeploymentWhatIfChange> Changes,
    DateTimeOffset CreatedAt);

public sealed record DeploymentApproval(
    Guid Id,
    Guid ProfileId,
    Guid PreviewId,
    string ProfileFingerprint,
    string ActorTenantId,
    string ActorObjectId,
    int CreateCount,
    int ModifyCount,
    int DeleteCount,
    DateTimeOffset ApprovedAt,
    DateTimeOffset? InvalidatedAt);

public enum DeploymentChangeType
{
    Create,
    Modify,
    Delete,
    NoChange,
    Ignore,
    Deploy,
    Unsupported,
}

public static class DeploymentProfileFingerprint
{
    public static string Compute(DeploymentProfile profile)
    {
        var canonical = string.Join(
            '\n',
            profile.ToPrerequisiteInputs()
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{item.Key}={item.Value.Trim()}"));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
