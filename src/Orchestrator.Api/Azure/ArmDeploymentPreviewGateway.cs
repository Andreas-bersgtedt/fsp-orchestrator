using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fsp.Orchestrator.Application.Deployments;

namespace Fsp.Orchestrator.Api.Azure;

public sealed class ArmDeploymentPreviewGateway(
    HttpClient client,
    IAzureTokenSessionStore sessions)
    : IArmDeploymentPreviewGateway
{
    private const string DeploymentApiVersion = "2025-04-01";
    private const int MaximumResponseBytes = 8_388_608;
    private const int MaximumPollAttempts = 60;

    public async Task<ArmDeploymentPreview> ValidateAndPreviewAsync(
        DeploymentProfile profile,
        DeploymentActor actor,
        CancellationToken cancellationToken)
    {
        var session = sessions.Retrieve(actor.TenantId, actor.ObjectId);
        await ValidateExistingResourcesAsync(
            session,
            profile,
            cancellationToken);
        var scope =
            $"/subscriptions/{profile.SubscriptionId:D}/resourceGroups/"
            + Uri.EscapeDataString(profile.ResourceGroupName)
            + "/providers/Microsoft.Resources/deployments/"
            + Uri.EscapeDataString(profile.DeploymentName);
        var body = CreateDeploymentBody(profile);
        var validation = await PostAsync(
            session,
            $"{scope}/validate?api-version={DeploymentApiVersion}",
            body,
            allowLongRunningOperation: true,
            cancellationToken);
        using (validation.Document)
        {
            EnsureValidationSucceeded(validation.Document.RootElement);
        }

        var whatIf = await PostAsync(
            session,
            $"{scope}/whatIf?api-version={DeploymentApiVersion}",
            body,
            allowLongRunningOperation: true,
            cancellationToken);
        using (whatIf.Document)
        {
            return new ArmDeploymentPreview(
                validation.CorrelationId,
                whatIf.CorrelationId,
                ParseChanges(whatIf.Document.RootElement));
        }
    }

    internal static string CreateDeploymentBody(DeploymentProfile profile)
    {
        var template = DeploymentTemplate.Create(profile);
        var body = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["mode"] = "Incremental",
                ["template"] = template,
                ["parameters"] = new JsonObject(),
            },
        };
        return body.ToJsonString();
    }

    private async Task ValidateExistingResourcesAsync(
        AzureAccessTokenSession session,
        DeploymentProfile profile,
        CancellationToken cancellationToken)
    {
        var references = new[]
        {
            (profile.ExistingVnetResourceId, "2024-05-01"),
            (profile.ExistingContainerRegistryResourceId, "2023-11-01-preview"),
            (profile.ExistingKeyVaultResourceId, "2024-11-01"),
        };
        foreach (var (resourceId, apiVersion) in references)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                continue;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{resourceId}?api-version={apiVersion}");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                sessions.Remove(session.TenantId, session.ObjectId);
                throw new AzureSignInRequiredException();
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ArmDeploymentPreviewException(
                    "deployment.existing_resource_not_found",
                    $"Azure resource '{resourceId}' was not found.",
                    StatusCodes.Status400BadRequest);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ArmDeploymentPreviewException(
                    response.StatusCode == HttpStatusCode.Forbidden
                        ? "azure.scope_denied"
                        : "azure.existing_resource_check_failed",
                    "Azure could not validate an existing resource reference.",
                    (int)response.StatusCode);
            }
        }
    }

    private async Task<ArmResponse> PostAsync(
        AzureAccessTokenSession session,
        string relativeUri,
        string body,
        bool allowLongRunningOperation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            session.AccessToken);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            body,
            Encoding.UTF8,
            "application/json");
        var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.Accepted
            || !allowLongRunningOperation)
        {
            return await ReadResponseAsync(
                session,
                response,
                cancellationToken);
        }

        var correlationId = CorrelationId(response);
        var pollingUri = PollingUri(response);
        response.Dispose();
        for (var attempt = 0; attempt < MaximumPollAttempts; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            using var poll = new HttpRequestMessage(HttpMethod.Get, pollingUri);
            poll.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);
            poll.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            response = await client.SendAsync(
                poll,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                response.Dispose();
                continue;
            }

            var final = await ReadResponseAsync(
                session,
                response,
                cancellationToken);
            return final with
            {
                CorrelationId = string.IsNullOrWhiteSpace(final.CorrelationId)
                    ? correlationId
                    : final.CorrelationId,
            };
        }

        throw new ArmDeploymentPreviewException(
            "azure.what_if_timeout",
            "Azure what-if did not complete within the polling limit.",
            StatusCodes.Status504GatewayTimeout);
    }

    private async Task<ArmResponse> ReadResponseAsync(
        AzureAccessTokenSession session,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                sessions.Remove(session.TenantId, session.ObjectId);
                throw new AzureSignInRequiredException();
            }

            await response.Content.LoadIntoBufferAsync(
                MaximumResponseBytes,
                cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = ErrorCode(document.RootElement)
                    ?? "azure.deployment_request_failed";
                document.Dispose();
                throw new ArmDeploymentPreviewException(
                    code,
                    "Azure rejected the deployment validation or what-if request.",
                    (int)response.StatusCode);
            }

            return new ArmResponse(document, CorrelationId(response));
        }
    }

    private static void EnsureValidationSucceeded(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error)
            || (root.TryGetProperty("properties", out var properties)
                && properties.TryGetProperty("error", out error)))
        {
            throw new ArmDeploymentPreviewException(
                ErrorCode(error) ?? "azure.deployment_validation_failed",
                "Azure deployment validation failed.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static DeploymentWhatIfChange[] ParseChanges(
        JsonElement root)
    {
        if (!root.TryGetProperty("properties", out var properties)
            || !properties.TryGetProperty("changes", out var changes)
            || changes.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "ARM what-if response has no properties.changes array.");
        }

        return changes.EnumerateArray()
            .Select(
                change => new DeploymentWhatIfChange(
                    RequiredString(change, "resourceId"),
                    ParseChangeType(RequiredString(change, "changeType")),
                    OptionalJson(change, "before"),
                    OptionalJson(change, "after"),
                    OptionalJson(change, "delta")))
            .OrderBy(item => item.ResourceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static DeploymentChangeType ParseChangeType(string value) =>
        Enum.TryParse<DeploymentChangeType>(
            value,
            ignoreCase: true,
            out var result)
            ? result
            : DeploymentChangeType.Unsupported;

    private static string RequiredString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var result)
            && result.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(result.GetString())
                ? result.GetString()!
                : throw new JsonException(
                    $"ARM response has no '{property}' value.");

    private static string? OptionalJson(JsonElement value, string property) =>
        value.TryGetProperty(property, out var result)
            && result.ValueKind is not JsonValueKind.Null
                ? result.GetRawText()
                : null;

    private static string? ErrorCode(JsonElement root)
    {
        var error = root.TryGetProperty("error", out var nested)
            ? nested
            : root;
        return error.TryGetProperty("code", out var code)
            && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
    }

    private static string CorrelationId(HttpResponseMessage response) =>
        response.Headers.TryGetValues(
            "x-ms-correlation-request-id",
            out var values)
            ? values.FirstOrDefault() ?? string.Empty
            : string.Empty;

    private static Uri PollingUri(HttpResponseMessage response)
    {
        var value = response.Headers.Location?.ToString();
        if (string.IsNullOrWhiteSpace(value)
            && response.Headers.TryGetValues(
                "Azure-AsyncOperation",
                out var values))
        {
            value = values.FirstOrDefault();
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(
                uri.Host,
                "management.azure.com",
                StringComparison.OrdinalIgnoreCase))
        {
            response.Dispose();
            throw new ArmDeploymentPreviewException(
                "azure.what_if_polling_invalid",
                "Azure returned an invalid what-if polling URI.",
                StatusCodes.Status502BadGateway);
        }

        return uri;
    }

    private sealed record ArmResponse(
        JsonDocument Document,
        string CorrelationId);
}

internal static class DeploymentTemplate
{
    public static JsonObject Create(DeploymentProfile profile)
    {
        var resources = new JsonArray
        {
            Identity(profile),
            VirtualNetwork(profile),
            ContainerRegistry(profile),
            KeyVault(profile),
            ManagedCluster(profile),
        };
        return new JsonObject
        {
            ["$schema"] =
                "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
            ["contentVersion"] = "1.0.0.0",
            ["metadata"] = new JsonObject
            {
                ["_generator"] = new JsonObject
                {
                    ["name"] = "bicep",
                    ["version"] = "0.39.26.0",
                    ["templateHash"] =
                        DeploymentProfileFingerprint.Compute(profile),
                },
            },
            ["resources"] = resources,
            ["outputs"] = new JsonObject
            {
                ["managedClusterResourceId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["value"] =
                        "[resourceId('Microsoft.ContainerService/managedClusters', '"
                        + Escape(profile.AksName)
                        + "')]",
                },
            },
        };
    }

    private static JsonObject Identity(DeploymentProfile profile) =>
        new()
        {
            ["type"] = "Microsoft.ManagedIdentity/userAssignedIdentities",
            ["apiVersion"] = "2023-01-31",
            ["name"] = $"{profile.AksName}-identity",
            ["location"] = profile.Location,
        };

    private static JsonObject VirtualNetwork(DeploymentProfile profile) =>
        new()
        {
            ["condition"] = string.IsNullOrWhiteSpace(
                profile.ExistingVnetResourceId),
            ["type"] = "Microsoft.Network/virtualNetworks",
            ["apiVersion"] = "2024-05-01",
            ["name"] = profile.VnetName,
            ["location"] = profile.Location,
            ["properties"] = new JsonObject
            {
                ["addressSpace"] = new JsonObject
                {
                    ["addressPrefixes"] = new JsonArray(profile.VnetCidr),
                },
                ["subnets"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = profile.SubnetName,
                        ["properties"] = new JsonObject
                        {
                            ["addressPrefix"] = profile.AksSubnetCidr,
                            ["privateEndpointNetworkPolicies"] = "Disabled",
                        },
                    },
                },
            },
        };

    private static JsonObject ContainerRegistry(DeploymentProfile profile) =>
        new()
        {
            ["condition"] = string.IsNullOrWhiteSpace(
                profile.ExistingContainerRegistryResourceId),
            ["type"] = "Microsoft.ContainerRegistry/registries",
            ["apiVersion"] = "2023-11-01-preview",
            ["name"] = RegistryName(profile.AksName),
            ["location"] = profile.Location,
            ["sku"] = new JsonObject { ["name"] = "Premium" },
            ["properties"] = new JsonObject
            {
                ["adminUserEnabled"] = false,
                ["publicNetworkAccess"] = "Disabled",
            },
        };

    private static JsonObject KeyVault(DeploymentProfile profile) =>
        new()
        {
            ["condition"] = string.IsNullOrWhiteSpace(
                profile.ExistingKeyVaultResourceId),
            ["type"] = "Microsoft.KeyVault/vaults",
            ["apiVersion"] = "2024-11-01",
            ["name"] = VaultName(profile.AksName),
            ["location"] = profile.Location,
            ["properties"] = new JsonObject
            {
                ["tenantId"] = "[subscription().tenantId]",
                ["sku"] = new JsonObject
                {
                    ["family"] = "A",
                    ["name"] = "standard",
                },
                ["enableRbacAuthorization"] = true,
                ["enablePurgeProtection"] = true,
                ["publicNetworkAccess"] = "Disabled",
            },
        };

    private static JsonObject ManagedCluster(DeploymentProfile profile)
    {
        var subnetId = string.IsNullOrWhiteSpace(
            profile.ExistingVnetResourceId)
            ? "[resourceId('Microsoft.Network/virtualNetworks/subnets', '"
                + Escape(profile.VnetName)
                + "', '"
                + Escape(profile.SubnetName)
                + "')]"
            : $"{profile.ExistingVnetResourceId}/subnets/{profile.SubnetName}";
        var dependencies = new JsonArray
        {
            "[resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', '"
                + Escape(profile.AksName)
                + "-identity')]",
        };
        if (string.IsNullOrWhiteSpace(profile.ExistingVnetResourceId))
        {
            dependencies.Add(
                "[resourceId('Microsoft.Network/virtualNetworks', '"
                + Escape(profile.VnetName)
                + "')]");
        }

        return new JsonObject
        {
            ["type"] = "Microsoft.ContainerService/managedClusters",
            ["apiVersion"] = "2025-01-01",
            ["name"] = profile.AksName,
            ["location"] = profile.Location,
            ["identity"] = new JsonObject
            {
                ["type"] = "UserAssigned",
                ["userAssignedIdentities"] = new JsonObject
                {
                    ["[resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', '"
                        + Escape(profile.AksName)
                        + "-identity')]"] = new JsonObject(),
                },
            },
            ["properties"] = new JsonObject
            {
                ["dnsPrefix"] = profile.AksName,
                ["kubernetesVersion"] = profile.KubernetesVersion,
                ["enableRBAC"] = true,
                ["apiServerAccessProfile"] = new JsonObject
                {
                    ["enablePrivateCluster"] = true,
                    ["privateDNSZone"] = "system",
                },
                ["agentPoolProfiles"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "system",
                        ["count"] = profile.NodeCount,
                        ["vmSize"] = profile.NodeVmSku,
                        ["mode"] = "System",
                        ["osType"] = "Linux",
                        ["type"] = "VirtualMachineScaleSets",
                        ["vnetSubnetID"] = subnetId,
                    },
                },
                ["networkProfile"] = new JsonObject
                {
                    ["networkPlugin"] = "azure",
                    ["networkPluginMode"] = "overlay",
                    ["networkPolicy"] = "azure",
                    ["podCidr"] = profile.PodCidr,
                    ["serviceCidr"] = profile.ServiceCidr,
                    ["dnsServiceIP"] = profile.DnsServiceIp,
                    ["outboundType"] = "loadBalancer",
                },
            },
            ["dependsOn"] = dependencies,
        };
    }

    private static string RegistryName(string aksName)
    {
        var normalized = new string(
                aksName.Where(char.IsLetterOrDigit).ToArray())
            .ToLowerInvariant()
            .PadRight(5, '0');
        return $"{normalized[..Math.Min(normalized.Length, 45)]}acr";
    }

    private static string VaultName(string aksName)
    {
        var normalized = aksName.Trim('-').ToLowerInvariant();
        return $"{normalized[..Math.Min(normalized.Length, 21)]}-kv";
    }

    private static string Escape(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
