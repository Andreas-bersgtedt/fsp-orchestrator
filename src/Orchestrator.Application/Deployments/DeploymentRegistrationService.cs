using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.Application.Deployments;

public sealed class DeploymentRegistrationService(
    IInventoryStore inventoryStore,
    IFspDeploymentProbe probe,
    TimeProvider timeProvider)
    : IDeploymentRegistrationService
{
    public async Task<DeploymentRegistrationResult> RegisterAsync(
        DeploymentRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var endpoint = Normalize(request.ManagerEndpoint);
        if (await inventoryStore.ExistsByManagerEndpointAsync(
                endpoint,
                cancellationToken))
        {
            throw new DeploymentRegistrationException(
                DeploymentRegistrationFailure.DuplicateEndpoint,
                "deployment.endpoint_duplicate",
                "The FSP endpoint is already registered.");
        }

        var result = await probe.ProbeAsync(
            new DeploymentProbeRequest(
                endpoint,
                request.CredentialKind,
                request.CredentialReference),
            cancellationToken);
        var observedAt = timeProvider.GetUtcNow();
        DeploymentInventoryItem deployment;
        try
        {
            deployment = await inventoryStore.RegisterAsync(
                new RegisterDeployment(
                    request.Id,
                    request.Name.Trim(),
                    result.Topology.ToString(),
                    endpoint,
                    request.AzureResourceId?.Trim(),
                    request.CredentialKind == FspCredentialKind.None
                        ? null
                        : request.CredentialKind.ToString(),
                    request.CredentialReference,
                    result.Components,
                    observedAt),
                cancellationToken);
        }
        catch (DuplicateManagerEndpointException)
        {
            throw new DeploymentRegistrationException(
                DeploymentRegistrationFailure.DuplicateEndpoint,
                "deployment.endpoint_duplicate",
                "The FSP endpoint is already registered.");
        }
        return new DeploymentRegistrationResult(
            deployment,
            result.Components);
    }

    private static void Validate(DeploymentRegistrationRequest request)
    {
        if (request.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Name)
            || request.Name.Length > 200)
        {
            Invalid("deployment.registration_invalid", "Deployment name is invalid.");
        }

        if (!request.ManagerEndpoint.IsAbsoluteUri
            || request.ManagerEndpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(request.ManagerEndpoint.Query)
            || !string.IsNullOrEmpty(request.ManagerEndpoint.Fragment)
            || request.ManagerEndpoint.UserInfo.Length > 0)
        {
            Invalid(
                "deployment.endpoint_invalid",
                "The Manager endpoint must be an absolute HTTPS origin without credentials, query, or fragment.");
        }

        if (request.AzureResourceId is { Length: > 2048 }
            || request.AzureResourceId is not null
                && !request.AzureResourceId.StartsWith(
                    "/subscriptions/",
                    StringComparison.OrdinalIgnoreCase))
        {
            Invalid(
                "deployment.azure_resource_invalid",
                "The Azure resource ID must be an absolute subscription resource ID.");
        }

        if (request.CredentialKind == FspCredentialKind.None
            && request.CredentialReference is not null
            || request.CredentialKind != FspCredentialKind.None
            && !IsKeyVaultSecretReference(request.CredentialReference))
        {
            Invalid(
                "deployment.credential_reference_invalid",
                "Credentials must use an Azure Key Vault secret reference.");
        }
    }

    private static bool IsKeyVaultSecretReference(Uri? reference)
    {
        if (reference is null
            || !reference.IsAbsoluteUri
            || reference.Scheme != Uri.UriSchemeHttps
            || !reference.Host.EndsWith(
                ".vault.azure.net",
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(reference.Query)
            || !string.IsNullOrEmpty(reference.Fragment))
        {
            return false;
        }

        var segments = reference.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length is 2 or 3
            && string.Equals(segments[0], "secrets", StringComparison.OrdinalIgnoreCase)
            && segments.All(segment => segment.Length > 0);
    }

    private static Uri Normalize(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint)
        {
            Path = endpoint.AbsolutePath.TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty,
        };
        if (string.IsNullOrEmpty(builder.Path))
        {
            builder.Path = "/";
        }

        return builder.Uri;
    }

    private static void Invalid(string code, string message)
    {
        throw new DeploymentRegistrationException(
            DeploymentRegistrationFailure.InvalidInput,
            code,
            message);
    }
}
