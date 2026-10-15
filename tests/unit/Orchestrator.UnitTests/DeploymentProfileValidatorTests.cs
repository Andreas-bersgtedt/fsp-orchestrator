using Fsp.Orchestrator.Application.Deployments;

namespace Fsp.Orchestrator.UnitTests;

public sealed class DeploymentProfileValidatorTests
{
    [Fact]
    public void ValidProfilePassesTypedValidation()
    {
        var errors = new DeploymentProfileValidator().Validate(ValidProfile());

        Assert.Empty(errors);
    }

    [Fact]
    public void InvalidSkuOverlappingCidrAndResourceIdAreReportedPrecisely()
    {
        var profile = ValidProfile() with
        {
            NodeVmSku = "not-a-sku",
            PodCidr = "10.0.0.0/24",
            ExistingKeyVaultResourceId =
                "/subscriptions/00000000-0000-0000-0000-000000000999/"
                + "resourceGroups/fsp/providers/Microsoft.KeyVault/vaults/fsp",
        };

        var errors = new DeploymentProfileValidator().Validate(profile);

        Assert.Contains(
            errors,
            error => error.Field == nameof(profile.NodeVmSku)
                && error.Code == "deployment.sku_invalid");
        Assert.Contains(
            errors,
            error => error.Field == nameof(profile.PodCidr)
                && error.Code == "deployment.cidr_overlap");
        Assert.Contains(
            errors,
            error => error.Field == nameof(profile.ExistingKeyVaultResourceId)
                && error.Code == "deployment.resource_id_invalid");
    }

    [Fact]
    public void FingerprintChangesForMaterialProfileInput()
    {
        var profile = ValidProfile();

        var original = DeploymentProfileFingerprint.Compute(profile);
        var changed = DeploymentProfileFingerprint.Compute(
            profile with { NodeCount = profile.NodeCount + 1 });

        Assert.NotEqual(original, changed);
    }

    private static DeploymentProfile ValidProfile() =>
        new(
            Guid.NewGuid(),
            "Production FSP",
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "fsp-rg",
            "fsp-deployment",
            "swedencentral",
            "1.32.7",
            "Standard_D4s_v5",
            3,
            12,
            "fsp-aks",
            "fsp-vnet",
            "aks-subnet",
            "10.0.0.0/16",
            "10.0.0.0/24",
            "10.1.0.0/16",
            "10.2.0.0/16",
            "10.2.0.10",
            "privatelink.swedencentral.azmk8s.io",
            null,
            null,
            null);
}

