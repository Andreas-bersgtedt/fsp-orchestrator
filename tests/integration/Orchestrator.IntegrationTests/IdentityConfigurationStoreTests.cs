using System.Security.AccessControl;
using System.Text.Json;
using Fsp.Orchestrator.Api.Hosting;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class IdentityConfigurationStoreTests
{
    [Fact]
    public async Task CommitAtomicallyStoresOnlyPublicIdentityConfiguration()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"fsp-identity-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new IdentityConfigurationStore(
                new HostingRuntime(
                    "https://localhost:9443",
                    directory,
                    DateTimeOffset.UtcNow.AddDays(1)));
            var configuration = new PersistedIdentityConfiguration(
                "https://login.microsoftonline.com/",
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "orchestrator.access_as_user",
                Guid.NewGuid().ToString(),
                DateTimeOffset.UtcNow);

            await store.CommitAsync(
                configuration,
                CancellationToken.None);

            var path = Path.Combine(
                directory,
                IdentityConfigurationStore.FileName);
            var json = await File.ReadAllTextAsync(
                path,
                CancellationToken.None);
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
}
