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
    : IIdentityConfigurationStore, IDisposable
{
    public const string FileName = "orchestrator.identity.json";
    private readonly SemaphoreSlim gate = new(1, 1);
    private byte[]? previousConfiguration;
    private bool previousConfigurationExisted;

    public void Dispose()
    {
        gate.Dispose();
    }

    public async Task CommitAsync(
        PersistedIdentityConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        var destination = Path.Combine(hostingRuntime.DataDirectory, FileName);
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            previousConfigurationExisted = File.Exists(destination);
            previousConfiguration = previousConfigurationExisted
                ? await File.ReadAllBytesAsync(destination, cancellationToken)
                : null;
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

            gate.Release();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var destination = Path.Combine(hostingRuntime.DataDirectory, FileName);
            if (!previousConfigurationExisted)
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                return;
            }

            var temporary = $"{destination}.{Guid.NewGuid():N}.rollback";
            try
            {
                await File.WriteAllBytesAsync(
                    temporary,
                    previousConfiguration
                        ?? throw new InvalidOperationException(
                            "Previous identity configuration is unavailable."),
                    cancellationToken);
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
        finally
        {
            previousConfiguration = null;
            previousConfigurationExisted = false;
            gate.Release();
        }
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
