using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Fsp.Orchestrator.Application.Prerequisites;

namespace Fsp.Orchestrator.Api.Azure;

public sealed class AzurePrerequisiteProbe(
    HttpClient client,
    IAzureTokenSessionStore sessions)
    : IAzurePrerequisiteProbe
{
    private const int MaximumResponseBytes = 1_048_576;

    public bool HasSession(string tenantId, string objectId)
    {
        try
        {
            _ = sessions.Retrieve(tenantId, objectId);
            return true;
        }
        catch (AzureSignInRequiredException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> GetUnregisteredProvidersAsync(
        string tenantId,
        string objectId,
        Guid subscriptionId,
        IReadOnlyList<string> providers,
        CancellationToken cancellationToken)
    {
        var session = Session(tenantId, objectId);
        var checks = providers.Select(
            async provider =>
            {
                using var document = await GetJsonAsync(
                    session,
                    $"/subscriptions/{subscriptionId:D}/providers/{provider}"
                        + "?api-version=2021-04-01",
                    cancellationToken);
                var state = document.RootElement.TryGetProperty(
                        "registrationState",
                        out var registrationState)
                    ? registrationState.GetString()
                    : null;
                return string.Equals(
                    state,
                    "Registered",
                    StringComparison.OrdinalIgnoreCase)
                    ? null
                    : provider;
            });
        return (await Task.WhenAll(checks))
            .Where(provider => provider is not null)
            .Select(provider => provider!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<AzurePermissionSnapshot> CheckPermissionsAsync(
        string tenantId,
        string objectId,
        Guid subscriptionId,
        string resourceGroup,
        IReadOnlyList<string> requiredActions,
        CancellationToken cancellationToken)
    {
        var session = Session(tenantId, objectId);
        var scope =
            $"/subscriptions/{subscriptionId:D}/resourceGroups/{Uri.EscapeDataString(resourceGroup)}";
        using var document = await GetJsonAsync(
            session,
            $"{scope}/providers/Microsoft.Authorization/permissions"
                + "?api-version=2022-04-01",
            cancellationToken);
        if (!document.RootElement.TryGetProperty("value", out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse();
        }

        var grants = values.EnumerateArray()
            .Select(
                permission => new PermissionGrant(
                    Strings(permission, "actions"),
                    Strings(permission, "notActions")))
            .ToArray();
        var denied = requiredActions
            .Where(
                action => !grants.Any(
                    grant => grant.Actions.Any(
                            pattern => Matches(pattern, action))
                        && !grant.NotActions.Any(
                            pattern => Matches(pattern, action))))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new AzurePermissionSnapshot(scope, denied);
    }

    public async Task<AzureQuotaSnapshot> GetRegionalVcpuQuotaAsync(
        string tenantId,
        string objectId,
        Guid subscriptionId,
        string location,
        CancellationToken cancellationToken)
    {
        var session = Session(tenantId, objectId);
        using var document = await GetJsonAsync(
            session,
            $"/subscriptions/{subscriptionId:D}/providers/Microsoft.Compute/"
                + $"locations/{Uri.EscapeDataString(location)}/usages"
                + "?api-version=2024-07-01",
            cancellationToken);
        if (!document.RootElement.TryGetProperty("value", out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse();
        }

        foreach (var usage in values.EnumerateArray())
        {
            var name = usage.TryGetProperty("name", out var nameObject)
                && nameObject.TryGetProperty("value", out var nameValue)
                    ? nameValue.GetString()
                    : null;
            if (string.Equals(name, "cores", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    name,
                    "Total Regional vCPUs",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new AzureQuotaSnapshot(
                    RequiredInt64(usage, "currentValue"),
                    RequiredInt64(usage, "limit"));
            }
        }

        throw new AzurePrerequisiteProbeException(
            "azure.quota_unknown",
            "Azure did not return regional vCPU quota.");
    }

    public async Task<IReadOnlyList<string>> GetKubernetesVersionsAsync(
        string tenantId,
        string objectId,
        Guid subscriptionId,
        string location,
        CancellationToken cancellationToken)
    {
        var session = Session(tenantId, objectId);
        using var document = await GetJsonAsync(
            session,
            $"/subscriptions/{subscriptionId:D}/providers/"
                + "Microsoft.ContainerService/"
                + $"locations/{Uri.EscapeDataString(location)}/orchestrators"
                + "?api-version=2025-01-01&resource-type=managedClusters",
            cancellationToken);
        if (!document.RootElement.TryGetProperty("orchestrators", out var versions)
            || versions.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse();
        }

        return versions.EnumerateArray()
            .Select(
                version => version.TryGetProperty(
                        "orchestratorVersion",
                        out var value)
                    ? value.GetString()
                    : null)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Select(version => version!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private AzureAccessTokenSession Session(string tenantId, string objectId)
    {
        try
        {
            return sessions.Retrieve(tenantId, objectId);
        }
        catch (AzureSignInRequiredException)
        {
            throw new AzurePrerequisiteProbeException(
                "azure.sign_in_required",
                "Azure sign-in is required.");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(
        AzureAccessTokenSession session,
        string relativeUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
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
            throw new AzurePrerequisiteProbeException(
                "azure.sign_in_required",
                "Azure sign-in is required.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AzurePrerequisiteProbeException(
                "azure.arm_check_failed",
                "Azure Resource Manager prerequisite request failed.");
        }

        await response.Content.LoadIntoBufferAsync(
            MaximumResponseBytes,
            cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        try
        {
            return await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
    }

    private static string[] Strings(
        JsonElement element,
        string property) =>
        element.TryGetProperty(property, out var values)
            && values.ValueKind == JsonValueKind.Array
                ? values.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString()!)
                    .ToArray()
                : [];

    private static long RequiredInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            && value.TryGetInt64(out var result)
                ? result
                : throw InvalidResponse();

    private static bool Matches(string pattern, string action)
    {
        if (pattern == "*")
        {
            return true;
        }

        return pattern.EndsWith("/*", StringComparison.Ordinal)
            ? action.StartsWith(
                pattern[..^1],
                StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, action, StringComparison.OrdinalIgnoreCase);
    }

    private static AzurePrerequisiteProbeException InvalidResponse() =>
        new(
            "azure.arm_response_invalid",
            "Azure Resource Manager returned an invalid prerequisite response.");

    private sealed record PermissionGrant(
        IReadOnlyList<string> Actions,
        IReadOnlyList<string> NotActions);
}
