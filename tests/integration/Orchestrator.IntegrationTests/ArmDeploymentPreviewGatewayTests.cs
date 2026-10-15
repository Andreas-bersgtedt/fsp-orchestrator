using System.Net;
using System.Text;
using Fsp.Orchestrator.Api.Azure;
using Fsp.Orchestrator.Application.Deployments;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class ArmDeploymentPreviewGatewayTests
{
    [Fact]
    public async Task ValidationAndWhatIfUseDelegatedSessionAndReturnStructuredChanges()
    {
        var profile = Profile();
        var requests = new List<(string Path, string Body)>();
        var existingReferences = new List<string>();
        var handler = new StubHandler(
            async request =>
            {
                if (request.Method == HttpMethod.Get)
                {
                    existingReferences.Add(request.RequestUri!.AbsolutePath);
                    return Json(
                        """{"id":"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/fsp"}""",
                        "resource-correlation");
                }

                requests.Add(
                    (
                        request.RequestUri!.AbsolutePath,
                        await request.Content!.ReadAsStringAsync()
                    ));
                return request.RequestUri.AbsolutePath.EndsWith(
                    "/validate",
                    StringComparison.Ordinal)
                    ? Json(
                        """{"properties":{"provisioningState":"Succeeded"}}""",
                        "validation-correlation")
                    : Json(
                        $$"""
                        {
                          "properties": {
                            "changes": [
                              {
                                "resourceId": "/subscriptions/{{profile.SubscriptionId}}/resourceGroups/fsp-rg/providers/Microsoft.ContainerService/managedClusters/fsp-aks",
                                "changeType": "Modify",
                                "before": { "properties": { "kubernetesVersion": "1.31.9" } },
                                "after": { "properties": { "kubernetesVersion": "1.32.7" } },
                                "delta": { "path": "properties.kubernetesVersion" }
                              },
                              {
                                "resourceId": "/subscriptions/{{profile.SubscriptionId}}/resourceGroups/fsp-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/fsp-aks-identity",
                                "changeType": "Create",
                                "after": { "location": "swedencentral" }
                              }
                            ]
                          }
                        }
                        """,
                        "what-if-correlation");
            });
        var sessions = new InMemoryAzureTokenSessionStore(TimeProvider.System);
        var actor = new DeploymentActor(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString());
        sessions.Store(
            new ValidatedAzureIdentity(
                actor.TenantId,
                actor.ObjectId,
                DateTimeOffset.UtcNow.AddMinutes(10)),
            "memory-only-arm-token");
        var gateway = new ArmDeploymentPreviewGateway(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://management.azure.com/"),
            },
            sessions);

        var result = await gateway.ValidateAndPreviewAsync(
            profile,
            actor,
            CancellationToken.None);

        Assert.Equal("validation-correlation", result.ValidationCorrelationId);
        Assert.Equal("what-if-correlation", result.WhatIfCorrelationId);
        Assert.Single(existingReferences);
        Assert.EndsWith(
            "/providers/Microsoft.KeyVault/vaults/fsp-kv",
            existingReferences[0],
            StringComparison.Ordinal);
        Assert.Equal(2, result.Changes.Count);
        Assert.Contains(
            result.Changes,
            change => change.ChangeType == DeploymentChangeType.Create);
        Assert.Contains(
            result.Changes,
            change => change.ChangeType == DeploymentChangeType.Modify
                && change.DeltaJson is not null);
        Assert.Collection(
            requests,
            request =>
            {
                Assert.EndsWith(
                    "/validate",
                    request.Path,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "\"_generator\":{\"name\":\"bicep\"",
                    request.Body,
                    StringComparison.Ordinal);
            },
            request => Assert.EndsWith(
                "/whatIf",
                request.Path,
                StringComparison.Ordinal));
        Assert.All(
            handler.AuthorizationParameters,
            value => Assert.Equal("memory-only-arm-token", value));
    }

    private static DeploymentProfile Profile() =>
        new(
            Guid.NewGuid(),
            "Production FSP",
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "fsp-rg",
            "fsp-deployment",
            "swedencentral",
            "1.32.7",
            "Standard_D4s_v5",
            3,
            12,
            "fsp-aks",
            "fsp-vnet",
            "aks-subnet",
            "10.0.0.0/16",
            "10.0.0.0/24",
            "10.1.0.0/16",
            "10.2.0.0/16",
            "10.2.0.10",
            "privatelink.swedencentral.azmk8s.io",
            null,
            null,
            "/subscriptions/00000000-0000-0000-0000-000000000001/"
                + "resourceGroups/fsp-rg/providers/Microsoft.KeyVault/vaults/fsp-kv");

    private static HttpResponseMessage Json(string json, string correlationId)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        response.Headers.Add("x-ms-correlation-request-id", correlationId);
        return response;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        public List<string?> AuthorizationParameters { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            AuthorizationParameters.Add(
                request.Headers.Authorization?.Parameter);
            return response(request);
        }
    }
}
