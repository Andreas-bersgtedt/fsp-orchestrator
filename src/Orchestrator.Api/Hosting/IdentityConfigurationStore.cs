using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fsp.Orchestrator.Api.Hosting;

public interface IIdentityConfigurationStore
{
    Task CommitAsync(
        PersistedIdentityConfiguration configuration,
        CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

public sealed class IdentityConfigurationStore(HostingRuntime hostingRuntime)
    : IIdentityConfigurationStore
{
    public const string FileName = "orchestrator.identity.json";

    public async Task CommitAsync(
        PersistedIdentityConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var destination = Path.Combine(hostingRuntime.DataDirectory, FileName);
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new PersistedIdentityConfigurationDocument(
                        new PersistedAuthenticationSection(configuration)),
                    SourceGenerationContext.Default
                        .PersistedIdentityConfigurationDocument,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, destination, overwrite: true);
            RestrictToCurrentIdentity(destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.Combine(hostingRuntime.DataDirectory, FileName);
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        return Task.CompletedTask;
    }

    private static void RestrictToCurrentIdentity(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User
            ?? throw new InvalidOperationException(
                "Could not resolve the Windows service identity.");
        var security = new FileSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(
            new FileSystemAccessRule(
                owner,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}

public sealed record PersistedIdentityConfiguration(
    string Instance,
    string TenantId,
    string ClientId,
    string SpaClientId,
    string ScopeName,
    string ValidatedByObjectId,
    DateTimeOffset ValidatedAt);

public sealed record PersistedIdentityConfigurationDocument(
    PersistedAuthenticationSection Authentication);

public sealed record PersistedAuthenticationSection(
    PersistedIdentityConfiguration Entra);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PersistedIdentityConfigurationDocument))]
internal sealed partial class SourceGenerationContext : JsonSerializerContext;
