using System.Net;
using System.Text;
using Fsp.Orchestrator.Api.Azure;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class AzurePrerequisiteProbeTests
{
    private static readonly Guid TenantId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ObjectId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid SubscriptionId =
        Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task ArmChecksReturnProviderPermissionQuotaAndVersionFacts()
    {
        var probe = Probe(
            request => request.RequestUri!.AbsolutePath switch
            {
                var path when path.EndsWith(
                    "/permissions",
                    StringComparison.Ordinal) =>
                    Json("""{"value":[{"actions":["*"],"notActions":[]}]}"""),
                var path when path.EndsWith(
                    "/usages",
                    StringComparison.Ordinal) =>
                    Json(
                        """
                        {
                          "value": [
                            {
                              "name": {"value":"cores"},
                              "currentValue": 12,
                              "limit": 20
                            }
                          ]
                        }
                        """),
                var path when path.EndsWith(
                    "/orchestrators",
                    StringComparison.Ordinal) =>
                    Json(
                        """
                        {
                          "orchestrators": [
                            {"orchestratorVersion":"1.32.7"}
                          ]
                        }
                        """),
                var path when path.Contains(
                    "/providers/Microsoft.",
                    StringComparison.Ordinal) =>
                    Json("""{"registrationState":"Registered"}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });

        var providers = await probe.GetUnregisteredProvidersAsync(
            TenantId.ToString(),
            ObjectId.ToString(),
            SubscriptionId,
            ["Microsoft.ContainerService", "Microsoft.Network"],
            CancellationToken.None);
        var permission = await probe.CheckPermissionsAsync(
            TenantId.ToString(),
            ObjectId.ToString(),
            SubscriptionId,
            "fsp",
            ["Microsoft.Resources/deployments/write"],
            CancellationToken.None);
        var quota = await probe.GetRegionalVcpuQuotaAsync(
            TenantId.ToString(),
            ObjectId.ToString(),
            SubscriptionId,
            "swedencentral",
            CancellationToken.None);
        var versions = await probe.GetKubernetesVersionsAsync(
            TenantId.ToString(),
            ObjectId.ToString(),
            SubscriptionId,
            "swedencentral",
            CancellationToken.None);

        Assert.Empty(providers);
        Assert.Empty(permission.DeniedActions);
        Assert.Equal(12, quota.CurrentValue);
        Assert.Equal(20, quota.Limit);
        Assert.Contains("1.32.7", versions);
    }

    private static AzurePrerequisiteProbe Probe(
        Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var sessions = new InMemoryAzureTokenSessionStore(TimeProvider.System);
        sessions.Store(
            new ValidatedAzureIdentity(
                TenantId.ToString(),
                ObjectId.ToString(),
                DateTimeOffset.UtcNow.AddMinutes(10)),
            "memory-only-token");
        return new AzurePrerequisiteProbe(
            new HttpClient(new StubHandler(response))
            {
                BaseAddress = new Uri("https://management.azure.com/"),
                Timeout = TimeSpan.FromSeconds(2),
            },
            sessions);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"),
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(
                "memory-only-token",
                request.Headers.Authorization?.Parameter);
            return Task.FromResult(response(request));
        }
    }
}
