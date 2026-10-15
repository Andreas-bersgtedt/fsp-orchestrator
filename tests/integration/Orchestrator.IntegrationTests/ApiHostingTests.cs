using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Api.Endpoints;
using Fsp.Orchestrator.Api.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class ApiHostingTests : IAsyncLifetime
{
    private readonly X509Certificate2 certificate = TestCertificate.Create("localhost");
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"fsp-orchestrator-api-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program>? factory;

    public Task InitializeAsync()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
        factory = CreateFactory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        factory?.Dispose();
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Remove(certificate);
        certificate.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task HealthReadinessAndSpaShareOneOrigin()
    {
        using var client = CreateClient();

        var health = await client.GetFromJsonAsync<HealthResponse>("/api/health");
        var readiness = await client.GetFromJsonAsync<ReadinessResponse>("/api/ready");
        var spa = await client.GetStringAsync("/");

        Assert.Equal("ok", health!.Status);
        Assert.Equal("ready", readiness!.Status);
        Assert.Equal(LoopbackHosting.Origin, readiness.Origin);
        Assert.Contains("<title>FSP Orchestrator</title>", spa, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/openapi/does-not-exist")]
    public async Task ProtectedRoutesReturnUnauthorizedWithoutToken(string path)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PublicAuthenticationConfigurationContainsNoCredentials()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/auth/config");
        var body = await response.Content.ReadAsStringAsync();
        var configuration =
            await response.Content.ReadFromJsonAsync<AuthenticationConfigurationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(configuration!.Configured);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrowserRouteFallsBackToSpa()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/deployments/example");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>FSP Orchestrator</title>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestartPreservesFilesInConfiguredStateDirectory()
    {
        using (var client = CreateClient())
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ready")).StatusCode);
        }

        var marker = Path.Combine(dataDirectory, "restart.marker");
        await File.WriteAllTextAsync(marker, "preserved");
        factory!.Dispose();
        factory = CreateFactory();

        using var restartedClient = CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await restartedClient.GetAsync("/api/ready")).StatusCode);
        Assert.Equal("preserved", await File.ReadAllTextAsync(marker));
    }

    private HttpClient CreateClient()
    {
        return factory!.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri(LoopbackHosting.Origin),
            });
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new TestApplicationFactory(
            certificate.Thumbprint,
            dataDirectory);
    }

    private sealed class TestApplicationFactory(
        string certificateThumbprint,
        string stateDirectory)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("urls", LoopbackHosting.Origin);
            builder.UseSetting(
                "Orchestrator:Certificate:Thumbprint",
                certificateThumbprint);
            builder.UseSetting(
                "Orchestrator:Certificate:StoreLocation",
                StoreLocation.CurrentUser.ToString());
            builder.UseSetting("Orchestrator:DataDirectory", stateDirectory);
        }
    }
}
