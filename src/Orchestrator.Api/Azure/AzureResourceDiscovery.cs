using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Fsp.Orchestrator.Api.Azure;

public interface IAzureResourceDiscovery
{
    Task<IReadOnlyList<AzureSubscription>> ListSubscriptionsAsync(
        AzureAccessTokenSession session,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AzureResource>> ListResourcesAsync(
        AzureAccessTokenSession session,
        Guid subscriptionId,
        string? resourceGroup,
        CancellationToken cancellationToken);
}

public sealed class AzureResourceDiscovery(HttpClient client)
    : IAzureResourceDiscovery
{
    private const int MaximumPages = 10;
    private const int MaximumResponseBytes = 2_097_152;
    private const string SubscriptionApiVersion = "2022-12-01";
    private const string ResourceApiVersion = "2021-04-01";

    public async Task<IReadOnlyList<AzureSubscription>> ListSubscriptionsAsync(
        AzureAccessTokenSession session,
        CancellationToken cancellationToken)
    {
        var values = await GetValuesAsync(
            session,
            new Uri(
                "https://management.azure.com/subscriptions"
                + $"?api-version={SubscriptionApiVersion}"),
            cancellationToken);
        return values
            .Select(
                value => new AzureSubscription(
                    Guid.Parse(RequiredString(value, "subscriptionId")),
                    RequiredString(value, "displayName"),
                    OptionalString(value, "state") ?? "Unknown",
                    $"/subscriptions/{RequiredString(value, "subscriptionId")}",
                    SubscriptionApiVersion))
            .Where(
                subscription => string.Equals(
                        subscription.State,
                        "Enabled",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        subscription.State,
                        "Warned",
                        StringComparison.OrdinalIgnoreCase))
            .OrderBy(subscription => subscription.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<AzureResource>> ListResourcesAsync(
        AzureAccessTokenSession session,
        Guid subscriptionId,
        string? resourceGroup,
        CancellationToken cancellationToken)
    {
        var subscriptions = await ListSubscriptionsAsync(
            session,
            cancellationToken);
        if (!subscriptions.Any(
                subscription => subscription.SubscriptionId == subscriptionId))
        {
            throw new AzureScopeDeniedException();
        }

        var scope = $"/subscriptions/{subscriptionId:D}";
        if (!string.IsNullOrWhiteSpace(resourceGroup))
        {
            ValidateResourceGroup(resourceGroup);
            scope += $"/resourceGroups/{Uri.EscapeDataString(resourceGroup)}";
        }

        var values = await GetValuesAsync(
            session,
            new Uri(
                $"https://management.azure.com{scope}/resources"
                + $"?api-version={ResourceApiVersion}"),
            cancellationToken);
        return values
            .Select(
                value => new AzureResource(
                    RequiredResourceId(value),
                    RequiredString(value, "name"),
                    RequiredString(value, "type"),
                    OptionalString(value, "location"),
                    ResourceApiVersion))
            .OrderBy(resource => resource.Type, StringComparer.Ordinal)
            .ThenBy(resource => resource.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<JsonElement>> GetValuesAsync(
        AzureAccessTokenSession session,
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var values = new List<JsonElement>();
        Uri? next = initialUri;
        for (var page = 0; next is not null && page < MaximumPages; page++)
        {
            ValidateArmUri(next);
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
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
                throw new AzureSignInRequiredException();
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new AzureScopeDeniedException();
            }

            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(
                MaximumResponseBytes,
                cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("value", out var pageValues)
                || pageValues.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException(
                    "The ARM list response has no value array.");
            }

            values.AddRange(
                pageValues.EnumerateArray().Select(value => value.Clone()));
            next = document.RootElement.TryGetProperty(
                    "nextLink",
                    out var nextLink)
                && nextLink.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nextLink.GetString())
                    ? new Uri(nextLink.GetString()!, UriKind.Absolute)
                    : null;
        }

        if (next is not null)
        {
            throw new InvalidOperationException(
                "ARM discovery exceeded the page limit.");
        }

        return values;
    }

    private static string RequiredResourceId(JsonElement value)
    {
        var id = RequiredString(value, "id");
        return id.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase)
            ? id
            : throw new JsonException("ARM returned an invalid resource ID.");
    }

    private static string RequiredString(JsonElement value, string property) =>
        OptionalString(value, property)
        ?? throw new JsonException(
            $"ARM returned no '{property}' value.");

    private static string? OptionalString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var result)
            && result.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(result.GetString())
                ? result.GetString()
                : null;

    private static void ValidateArmUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(
                uri.Host,
                "management.azure.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ARM pagination returned an unsupported host.");
        }
    }

    private static void ValidateResourceGroup(string resourceGroup)
    {
        if (resourceGroup.Length > 90
            || resourceGroup.Any(
                character => char.IsControl(character)
                    || character is '/' or '\\' or '?' or '#'))
        {
            throw new ArgumentException(
                "The resource group name is invalid.",
                nameof(resourceGroup));
        }
    }
}

public sealed record AzureSubscription(
    Guid SubscriptionId,
    string DisplayName,
    string State,
    string ResourceId,
    string ApiVersion);

public sealed record AzureResource(
    string ResourceId,
    string Name,
    string Type,
    string? Location,
    string ApiVersion);

public sealed class AzureScopeDeniedException()
    : Exception("The Azure scope is not available to the signed-in operator.");
