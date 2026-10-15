using System.Net;
using System.Security.Authentication;
using System.Text;
using Fsp.Orchestrator.Application.Deployments;
using Fsp.Orchestrator.Application.Persistence;
using Fsp.Orchestrator.Infrastructure.Deployments;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class FspDeploymentProbeTests
{
    [Fact]
    public async Task LiteContractDetectsTopologyAndVersion()
    {
        var probe = Probe(
            request => request.RequestUri!.AbsolutePath switch
            {
                "/healthz" => Json("""{"status":"ok"}"""),
                "/readyz" => Json("""{"status":"ready"}"""),
                "/openapi.json" => Json(OpenApi(
                    "Fabric Shortcut Proxy (POC)",
                    "\"/\":{},\"/healthz\":{},\"/readyz\":{}")),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });

        var result = await probe.ProbeAsync(
            new DeploymentProbeRequest(
                new Uri("https://lite.example.test"),
                FspCredentialKind.None,
                null),
            CancellationToken.None);

        Assert.Equal(FspTopology.Lite, result.Topology);
        var component = Assert.Single(result.Components);
        Assert.Equal("lite", component.Name);
        Assert.Equal("3.1.0", component.Version);
    }

    [Fact]
    public async Task EnterpriseContractProvesIdentityAndObservesAgentVersion()
    {
        var resolver = new TestCredentialResolver("safe-test-token");
        var probe = Probe(
            request => request.RequestUri!.AbsolutePath switch
            {
                "/healthz" => Json("""{"status":"ok"}"""),
                "/readyz" => Json("""{"status":"ready"}"""),
                "/openapi.json" => Json(OpenApi(
                    "Fabric Shortcut Proxy: Manager",
                    "\"/agents\":{},\"/healthz\":{},\"/readyz\":{}")),
                "/agents" when request.Headers.Authorization?.Scheme == "Bearer"
                    && request.Headers.Authorization.Parameter == "safe-test-token" =>
                    Json(
                        """
                        {
                          "agents": [
                            {
                              "agent_id": "agent-a",
                              "server_version": "3.1.0"
                            }
                          ],
                          "dead": []
                        }
                        """),
                _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            },
            resolver);

        var result = await probe.ProbeAsync(
            new DeploymentProbeRequest(
                new Uri("https://manager.example.test"),
                FspCredentialKind.Bearer,
                new Uri(
                    "https://example.vault.azure.net/secrets/manager-token")),
            CancellationToken.None);

        Assert.Equal(FspTopology.Enterprise, result.Topology);
        Assert.Equal(2, result.Components.Count);
        Assert.Contains(
            result.Components,
            component => component.Name == "manager"
                && component.Version == "3.1.0");
        Assert.Contains(
            result.Components,
            component => component.Name == "agent-a"
                && component.Version == "3.1.0");
        Assert.True(resolver.Executed);
    }

    [Fact]
    public async Task IncompatibleVersionUsesStableFailure()
    {
        var probe = Probe(
            request => request.RequestUri!.AbsolutePath switch
            {
                "/healthz" => Json("""{"status":"ok"}"""),
                "/readyz" => Json("""{"status":"ready"}"""),
                "/openapi.json" => Json(
                    """
                    {
                      "info": {
                        "title": "Fabric Shortcut Proxy (POC)",
                        "version": "3.0.1",
                        "x-fsp-release": "3.0.1"
                      },
                      "paths": {
                        "/": {},
                        "/healthz": {},
                        "/readyz": {}
                      }
                    }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });

        var exception = await Assert.ThrowsAsync<DeploymentRegistrationException>(
            () => probe.ProbeAsync(
                new DeploymentProbeRequest(
                    new Uri("https://lite.example.test"),
                    FspCredentialKind.None,
                    null),
                CancellationToken.None));

        Assert.Equal("deployment.version_incompatible", exception.Code);
    }

    [Fact]
    public async Task TlsFailureUsesStableFailureWithoutProviderDetails()
    {
        var probe = Probe(
            _ => throw new HttpRequestException(
                "sensitive provider detail",
                new AuthenticationException("sensitive TLS detail")));

        var exception = await Assert.ThrowsAsync<DeploymentRegistrationException>(
            () => probe.ProbeAsync(
                new DeploymentProbeRequest(
                    new Uri("https://lite.example.test"),
                    FspCredentialKind.None,
                    null),
                CancellationToken.None));

        Assert.Equal("deployment.tls_validation_failed", exception.Code);
        Assert.DoesNotContain(
            "sensitive",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnterpriseObservationCollectsHealthFleetQueueAndVersions()
    {
        var probe = Probe(
            request => request.RequestUri!.AbsolutePath switch
            {
                "/healthz" => Json("""{"status":"ok"}"""),
                "/readyz" => Json("""{"status":"ready"}"""),
                "/openapi.json" => Json(OpenApi(
                    "Fabric Shortcut Proxy: Manager",
                    "\"/agents\":{},\"/healthz\":{},\"/readyz\":{}")),
                "/agents" => Json(
                    """
                    {
                      "agents": [
                        {
                          "agent_id": "agent-a",
                          "server_version": "3.1.0"
                        }
                      ],
                      "dead": [{}]
                    }
                    """),
                "/control/work-queue" => Json(
                    """
                    {
                      "requests": [{}, {}],
                      "tasks": [{}],
                      "scheduler": "running"
                    }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            },
            new TestCredentialResolver("safe-test-token"));
        var deployment = new DeploymentInventoryItem(
            Guid.NewGuid(),
            "Enterprise",
            "Enterprise",
            new Uri("https://manager.example.test"),
            null,
            "Bearer",
            new Uri("https://example.vault.azure.net/secrets/manager"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1);

        var snapshot = await probe.ObserveAsync(
            deployment,
            CancellationToken.None);

        Assert.Contains(
            snapshot.Values,
            value => value.Name == "health" && value.Version == "ok");
        Assert.Contains(
            snapshot.Values,
            value => value.Name == "fleet"
                && value.Version == "active=1;dead=1");
        Assert.Contains(
            snapshot.Values,
            value => value.Name == "queue"
                && value.Version == "requests=2;tasks=1");
        Assert.Contains(
            snapshot.Values,
            value => value.Name == "agent-a"
                && value.Version == "3.1.0");
    }

    private static FspDeploymentProbe Probe(
        Func<HttpRequestMessage, HttpResponseMessage> response,
        TestCredentialResolver? resolver = null) =>
        new(
            new HttpClient(new StubHandler(response))
            {
                Timeout = TimeSpan.FromSeconds(2),
            },
            resolver ?? new TestCredentialResolver(null));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"),
        };

    private static string OpenApi(string title, string paths) =>
        $$"""
        {
          "info": {
            "title": "{{title}}",
            "version": "3.1.0",
            "x-fsp-release": "3.1.0"
          },
          "paths": {
            {{paths}}
          }
        }
        """;

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }

    private sealed class TestCredentialResolver(string? secret)
        : IFspCredentialResolver
    {
        public bool Executed { get; private set; }

        public Task<string?> ResolveAsync(
            FspCredentialKind kind,
            Uri? reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Executed = true;
            return Task.FromResult(secret);
        }
    }
}
