using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Fsp.Orchestrator.Application.Deployments;

namespace Fsp.Orchestrator.Infrastructure.Deployments;

public sealed class AzureKeyVaultCredentialResolver : IFspCredentialResolver
{
    private readonly TokenCredential credential = new DefaultAzureCredential(
        new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true,
        });

    public async Task<string?> ResolveAsync(
        FspCredentialKind kind,
        Uri? reference,
        CancellationToken cancellationToken)
    {
        if (kind == FspCredentialKind.None)
        {
            return null;
        }

        if (reference is null)
        {
            throw IdentityRejected();
        }

        var segments = reference.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is not (2 or 3))
        {
            throw IdentityRejected();
        }

        try
        {
            var client = new SecretClient(
                new Uri($"{reference.Scheme}://{reference.Host}"),
                credential);
            KeyVaultSecret secret = segments.Length == 3
                ? await client.GetSecretAsync(
                    segments[1],
                    segments[2],
                    cancellationToken)
                : await client.GetSecretAsync(
                    segments[1],
                    cancellationToken: cancellationToken);
            return string.IsNullOrWhiteSpace(secret.Value)
                ? throw IdentityRejected()
                : secret.Value;
        }
        catch (RequestFailedException)
        {
            throw IdentityRejected();
        }
        catch (AuthenticationFailedException)
        {
            throw IdentityRejected();
        }
    }

    private static DeploymentRegistrationException IdentityRejected() =>
        new(
            DeploymentRegistrationFailure.IdentityRejected,
            "deployment.credential_unavailable",
            "The credential reference could not be resolved with the Orchestrator identity.");
}
