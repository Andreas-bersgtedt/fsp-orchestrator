using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Api.Endpoints;
using Fsp.Orchestrator.Api.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class AuthenticationPolicyTests : IAsyncLifetime, IDisposable
{
    private const string TenantId = "00000000-0000-0000-0000-000000000001";
    private const string ApiClientId = "00000000-0000-0000-0000-000000000002";
    private const string SpaClientId = "00000000-0000-0000-0000-000000000003";
    private const string Scope = "orchestrator.access_as_user";

    private readonly X509Certificate2 certificate = TestCertificate.Create("localhost");
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"fsp-orchestrator-auth-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program>? factory;
    private bool disposed;

    public Task InitializeAsync()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
        factory = new AuthenticationApplicationFactory(
            certificate.Thumbprint,
            dataDirectory);
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
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SessionReturnsUnauthorizedWithoutAuthentication()
    {
        using var client = factory!.CreateClient();

        var response = await client.GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SessionRejectsUserWithoutAnOrchestratorRole()
    {
        using var client = CreateAuthenticatedClient(role: string.Empty);

        var response = await client.GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong.scope", SpaClientId, TenantId)]
    [InlineData(Scope, "00000000-0000-0000-0000-000000000999", TenantId)]
    [InlineData(Scope, SpaClientId, "00000000-0000-0000-0000-000000000999")]
    public async Task SessionRejectsInvalidScopeClientOrTenant(
        string scopes,
        string clientId,
        string tenantId)
    {
        using var client = CreateAuthenticatedClient(
            OrchestratorRoles.Reader,
            scopes,
            clientId,
            tenantId);

        var response = await client.GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(OrchestratorRoles.Reader)]
    [InlineData(OrchestratorRoles.Operator)]
    [InlineData(OrchestratorRoles.ConfigurationAdmin)]
    [InlineData(OrchestratorRoles.DeploymentAdmin)]
    [InlineData(OrchestratorRoles.SecurityAdmin)]
    public async Task EachRoleHasAnIndependentTestedPolicy(string role)
    {
        using var client = CreateAuthenticatedClient(role);

        var session = await client.GetFromJsonAsync<SessionResponse>("/api/session");

        Assert.NotNull(session);
        Assert.Single(session.Roles);
        Assert.Equal(role, session.Roles[0]);
        Assert.True(session.Permissions[role]);
        Assert.All(
            OrchestratorRoles.All.Where(candidate => candidate != role),
            candidate => Assert.False(session.Permissions[candidate]));
    }

    [Fact]
    public async Task OpenApiRequiresReaderRole()
    {
        using var operatorClient = CreateAuthenticatedClient(OrchestratorRoles.Operator);
        using var readerClient = CreateAuthenticatedClient(OrchestratorRoles.Reader);

        var denied = await operatorClient.GetAsync("/openapi/v1.json");
        var allowed = await readerClient.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task PublicConfigurationContainsOnlyPkceIdentifiers()
    {
        using var client = factory!.CreateClient();

        var configuration =
            await client.GetFromJsonAsync<AuthenticationConfigurationResponse>(
                "/api/auth/config");

        Assert.True(configuration!.Configured);
        Assert.Equal(TenantId, configuration.TenantId);
        Assert.Equal(ApiClientId, configuration.ApiClientId);
        Assert.Equal(SpaClientId, configuration.SpaClientId);
        Assert.Equal($"api://{ApiClientId}/{Scope}", configuration.ApiScope);
        Assert.Equal($"{LoopbackHosting.Origin}/auth/callback", configuration.RedirectUri);
    }

    [Fact]
    public void BearerValidationRequiresAudienceIssuerLifetimeAndSignedTokens()
    {
        _ = factory!.Server;
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.RequireExpirationTime);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
        Assert.Contains(ApiClientId, options.TokenValidationParameters.ValidAudiences);
        Assert.Contains(
            $"api://{ApiClientId}",
            options.TokenValidationParameters.ValidAudiences);
    }

    private HttpClient CreateAuthenticatedClient(
        string role,
        string scopes = Scope,
        string clientId = SpaClientId,
        string tenantId = TenantId)
    {
        var client = factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenantId);
        client.DefaultRequestHeaders.Add("X-Test-Client", clientId);
        client.DefaultRequestHeaders.Add("X-Test-Scopes", scopes);
        client.DefaultRequestHeaders.Add("X-Test-Roles", role);
        return client;
    }

    private sealed class AuthenticationApplicationFactory(
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
            builder.UseSetting("Authentication:Entra:TenantId", TenantId);
            builder.UseSetting("Authentication:Entra:ClientId", ApiClientId);
            builder.UseSetting("Authentication:Entra:SpaClientId", SpaClientId);
            builder.UseSetting("Authentication:Entra:ScopeName", Scope);
            builder.ConfigureTestServices(
                services =>
                {
                    services.AddAuthentication(
                            options =>
                            {
                                options.DefaultAuthenticateScheme =
                                    TestAuthenticationHandler.Scheme;
                                options.DefaultChallengeScheme =
                                    TestAuthenticationHandler.Scheme;
                                options.DefaultForbidScheme =
                                    TestAuthenticationHandler.Scheme;
                            })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                            TestAuthenticationHandler.Scheme,
                            _ => { });
                });
        }
    }
}
