using System.Net;
using System.Text;
using Fsp.Orchestrator.Api.Azure;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class AzureResourceDiscoveryTests
{
    [Fact]
    public async Task DiscoveryReturnsStableIdsAndApiMetadata()
    {
        var subscriptionId = Guid.NewGuid();
        var discovery = Discovery(
            request => request.RequestUri!.AbsolutePath switch
            {
                "/subscriptions" => Json(
                    $$"""
                    {
                      "value": [
                        {
                          "subscriptionId": "{{subscriptionId}}",
                          "displayName": "Production",
                          "state": "Enabled"
                        },
                        {
                          "subscriptionId": "{{Guid.NewGuid()}}",
                          "displayName": "Disabled",
                          "state": "Disabled"
                        }
                      ]
                    }
                    """),
                var path when path ==
                    $"/subscriptions/{subscriptionId:D}/resourceGroups/fsp/resources" =>
                    Json(
                        $$"""
                        {
                          "value": [
                            {
                              "id": "/subscriptions/{{subscriptionId}}/resourceGroups/fsp/providers/Microsoft.ContainerService/managedClusters/aks",
                              "name": "aks",
                              "type": "Microsoft.ContainerService/managedClusters",
                              "location": "swedencentral"
                            }
                          ]
                        }
                        """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        var session = Session();

        var subscriptions = await discovery.ListSubscriptionsAsync(
            session,
            CancellationToken.None);
        var resources = await discovery.ListResourcesAsync(
            session,
            subscriptionId,
            "fsp",
            CancellationToken.None);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal($"/subscriptions/{subscriptionId}", subscription.ResourceId);
        Assert.Equal("2022-12-01", subscription.ApiVersion);
        var resource = Assert.Single(resources);
        Assert.StartsWith(
            $"/subscriptions/{subscriptionId}/",
            resource.ResourceId,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("2021-04-01", resource.ApiVersion);
    }

    [Fact]
    public async Task ResourceScopeOutsideVisibleSubscriptionsIsDenied()
    {
        var allowedSubscription = Guid.NewGuid();
        var discovery = Discovery(
            _ => Json(
                $$"""
                {
                  "value": [
                    {
                      "subscriptionId": "{{allowedSubscription}}",
                      "displayName": "Allowed",
                      "state": "Enabled"
                    }
                  ]
                }
                """));

        await Assert.ThrowsAsync<AzureScopeDeniedException>(
            () => discovery.ListResourcesAsync(
                Session(),
                Guid.NewGuid(),
                null,
                CancellationToken.None));
    }

    private static AzureResourceDiscovery Discovery(
        Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(
            new HttpClient(new StubHandler(response))
            {
                Timeout = TimeSpan.FromSeconds(2),
            });

    private static AzureAccessTokenSession Session() =>
        new(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            "memory-only-token",
            DateTimeOffset.UtcNow.AddMinutes(10));

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
