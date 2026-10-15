using System.Security.AccessControl;
using System.Text.Json;
using Fsp.Orchestrator.Api.Hosting;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class IdentityConfigurationStoreTests
{
    [Fact]
    public async Task CommitAtomicallyStoresOnlyPublicIdentityConfiguration()
    {
        var directory = CreateTestDirectory("store");
        try
        {
            using var store = CreateStore(directory);
            var configuration = Configuration(Guid.NewGuid().ToString());

            await store.CommitAsync(configuration, CancellationToken.None);

            var path = Path.Combine(
                directory,
                IdentityConfigurationStore.FileName);
            var json = await File.ReadAllTextAsync(path, CancellationToken.None);
            using var document = JsonDocument.Parse(json);
            var entra = document.RootElement
                .GetProperty("Authentication")
                .GetProperty("Entra");
            Assert.Equal(configuration.TenantId, entra.GetProperty("TenantId").GetString());
            Assert.Equal(configuration.ClientId, entra.GetProperty("ClientId").GetString());
            Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            if (OperatingSystem.IsWindows())
            {
                var security = new FileInfo(path).GetAccessControl();
                Assert.True(security.AreAccessRulesProtected);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RollbackRestoresPreviousIdentityConfiguration()
    {
        var directory = CreateTestDirectory("rollback");
        try
        {
            using var store = CreateStore(directory);
            var original = Configuration(Guid.NewGuid().ToString());
            await store.CommitAsync(original, CancellationToken.None);
            var replacement = Configuration(Guid.NewGuid().ToString());
            await store.CommitAsync(replacement, CancellationToken.None);

            await store.RollbackAsync(CancellationToken.None);

            var json = await File.ReadAllTextAsync(
                Path.Combine(directory, IdentityConfigurationStore.FileName));
            using var document = JsonDocument.Parse(json);
            Assert.Equal(
                original.TenantId,
                document.RootElement
                    .GetProperty("Authentication")
                    .GetProperty("Entra")
                    .GetProperty("TenantId")
                    .GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IdentityConfigurationStore CreateStore(string directory) =>
        new(
            new HostingRuntime(
                "https://localhost:9443",
                directory,
                "CN=localhost",
                DateTimeOffset.UtcNow.AddDays(1)));

    private static string CreateTestDirectory(string suffix)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"fsp-identity-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static PersistedIdentityConfiguration Configuration(string tenantId) =>
        new(
            "https://login.microsoftonline.com/",
            tenantId,
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            "orchestrator.access_as_user",
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow);
}
