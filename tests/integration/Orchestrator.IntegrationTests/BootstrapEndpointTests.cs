using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Api.Endpoints;
using Fsp.Orchestrator.Api.Hosting;
using Fsp.Orchestrator.Application.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class BootstrapEndpointTests : IAsyncLifetime, IDisposable
{
    private readonly string tenantId = Guid.NewGuid().ToString();
    private readonly string spaClientId = Guid.NewGuid().ToString();
    private readonly string apiClientId = Guid.NewGuid().ToString();
    private readonly string operatorObjectId = Guid.NewGuid().ToString();
    private readonly X509Certificate2 certificate = TestCertificate.Create("localhost");
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"fsp-orchestrator-bootstrap-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program>? factory;
    private bool disposed;

    public Task InitializeAsync()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
        factory = new BootstrapApplicationFactory(
            certificate.Thumbprint,
            dataDirectory,
            new CandidateOperatorIdentity(
                operatorObjectId,
                tenantId,
                [OrchestratorRoles.SecurityAdmin]),
            restartThrows: false);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
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
    }

    [Fact]
    public async Task CommitRejectsRequestWithoutCandidateApiToken()
    {
        using var client = factory!.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/identity/registrations/apply",
            ValidCommitRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(
            File.Exists(
                Path.Combine(
                    dataDirectory,
                    IdentityConfigurationStore.FileName)));
    }

    [Fact]
    public async Task ValidCandidateTokenCommitsOnlyPublicConfiguration()
    {
        using var client = factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "candidate-api-token");

        var response = await client.PostAsJsonAsync(
            "/api/identity/registrations/apply",
            ValidCommitRequest());
        var result =
            await response.Content.ReadFromJsonAsync<BootstrapCommitResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result!.Committed);
        Assert.True(result.RestartRequired);
        var json = await File.ReadAllTextAsync(
            Path.Combine(
                dataDirectory,
                IdentityConfigurationStore.FileName));
        Assert.Contains(tenantId, json, StringComparison.Ordinal);
        Assert.Contains(spaClientId, json, StringComparison.Ordinal);
        Assert.Contains(apiClientId, json, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate-api-token", json, StringComparison.Ordinal);

        factory.Dispose();
        factory = new BootstrapApplicationFactory(
            certificate.Thumbprint,
            dataDirectory,
            new CandidateOperatorIdentity(
                operatorObjectId,
                tenantId,
                [OrchestratorRoles.SecurityAdmin]),
            restartThrows: false);
        using var restartedClient = factory.CreateClient();
        var activeConfiguration =
            await restartedClient.GetFromJsonAsync<AuthenticationConfigurationResponse>(
                "/api/auth/config");
        Assert.True(activeConfiguration!.Configured);
        Assert.Equal(tenantId, activeConfiguration.TenantId);
        Assert.Equal(spaClientId, activeConfiguration.SpaClientId);
        Assert.Equal(apiClientId, activeConfiguration.ApiClientId);
    }

    [Fact]
    public async Task RestartSchedulingFailureRollsBackCommittedConfiguration()
    {
        factory!.Dispose();
        factory = new BootstrapApplicationFactory(
            certificate.Thumbprint,
            dataDirectory,
            new CandidateOperatorIdentity(
                operatorObjectId,
                tenantId,
                [OrchestratorRoles.SecurityAdmin]),
            restartThrows: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "candidate-api-token");

        var response = await client.PostAsJsonAsync(
            "/api/identity/registrations/apply",
            ValidCommitRequest());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(
            File.Exists(
                Path.Combine(
                    dataDirectory,
                    IdentityConfigurationStore.FileName)));
    }

    private BootstrapCommitRequest ValidCommitRequest()
    {
        var scopeId = Guid.NewGuid();
        var registration = new ValidateEntraRegistrationsRequest
        {
            TenantId = tenantId,
            SignedInTenantId = tenantId,
            Spa = new EntraApplicationRegistrationRequest
            {
                ObjectId = Guid.NewGuid().ToString(),
                AppId = spaClientId,
                DisplayName = "FSP Orchestrator SPA",
                SignInAudience = EntraRegistrationContract.SingleTenantAudience,
                SpaRedirectUris = [EntraRegistrationContract.RedirectUri],
                IdentifierUris = [],
                DelegatedScopes = [],
                AppRoles = [],
                PreAuthorizedApplications = [],
                PasswordCredentialCount = 0,
                KeyCredentialCount = 0,
            },
            Api = new EntraApplicationRegistrationRequest
            {
                ObjectId = Guid.NewGuid().ToString(),
                AppId = apiClientId,
                DisplayName = "FSP Orchestrator API",
                SignInAudience = EntraRegistrationContract.SingleTenantAudience,
                SpaRedirectUris = [],
                IdentifierUris = [$"api://{apiClientId}"],
                DelegatedScopes =
                [
                    new EntraDelegatedScopeRequest(
                        scopeId,
                        EntraRegistrationContract.ScopeName,
                        "User",
                        true),
                ],
                AppRoles = EntraRegistrationContract.Roles
                    .Select(
                        role => new EntraAppRoleRequest(
                            Guid.NewGuid(),
                            role,
                            ["User"],
                            true))
                    .ToArray(),
                PreAuthorizedApplications =
                [
                    new EntraPreAuthorizedApplicationRequest(
                        spaClientId,
                        [scopeId]),
                ],
                PasswordCredentialCount = 0,
                KeyCredentialCount = 0,
            },
            SpaServicePrincipalExists = true,
            ApiServicePrincipalExists = true,
            SignedInUserRoles = [OrchestratorRoles.SecurityAdmin],
        };
        return new BootstrapCommitRequest
        {
            Candidate = new CandidateIdentityConfigurationRequest
            {
                TenantId = tenantId,
                SpaClientId = spaClientId,
                ApiClientId = apiClientId,
                ScopeName = EntraRegistrationContract.ScopeName,
            },
            Registration = registration,
        };
    }

    private sealed class BootstrapApplicationFactory(
        string certificateThumbprint,
        string stateDirectory,
        CandidateOperatorIdentity identity,
        bool restartThrows)
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
            builder.ConfigureTestServices(
                services =>
                {
                    services.RemoveAll<ICandidateApiTokenValidator>();
                    services.RemoveAll<IApplicationRestartScheduler>();
                    services.AddSingleton<ICandidateApiTokenValidator>(
                        new TestCandidateTokenValidator(identity));
                    services.AddSingleton<IApplicationRestartScheduler, TestRestartScheduler>();
                    services.AddSingleton(
                        new RestartTestBehavior(restartThrows));
                });
        }

        private sealed record RestartTestBehavior(bool Throws);

        private sealed class TestRestartScheduler(RestartTestBehavior behavior)
            : IApplicationRestartScheduler
        {
            public void ScheduleRestart()
            {
                if (behavior.Throws)
                {
                    throw new InvalidOperationException("Restart test failure.");
                }
            }
        }
    }

    private sealed class TestCandidateTokenValidator(
        CandidateOperatorIdentity identity)
        : ICandidateApiTokenValidator
    {
        public Task<CandidateOperatorIdentity> ValidateAsync(
            string accessToken,
            CandidateIdentityConfiguration candidate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(identity);
        }
    }
}
