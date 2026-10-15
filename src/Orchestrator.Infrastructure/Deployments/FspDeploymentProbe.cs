using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Fsp.Orchestrator.Application.Deployments;
using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.Infrastructure.Deployments;

public sealed class FspDeploymentProbe(
    HttpClient client,
    IFspCredentialResolver credentialResolver)
    : IFspDeploymentProbe, IFspObservationProbe
{
    private const string SupportedVersion = "3.1.0";
    private const int MaximumResponseBytes = 1_048_576;

    public Task<DeploymentProbeResult> ProbeAsync(
        DeploymentProbeRequest request,
        CancellationToken cancellationToken) =>
        MapErrorsAsync(
            () => ProbeCoreAsync(request, cancellationToken),
            cancellationToken);

    public Task<DeploymentObservationSnapshot> ObserveAsync(
        DeploymentInventoryItem deployment,
        CancellationToken cancellationToken) =>
        MapErrorsAsync(
            () => ObserveCoreAsync(deployment, cancellationToken),
            cancellationToken);

    private async Task<DeploymentProbeResult> ProbeCoreAsync(
        DeploymentProbeRequest request,
        CancellationToken cancellationToken)
    {
        await RequireStatusAsync(
            Endpoint(request.ManagerEndpoint, "healthz"),
            "ok",
            cancellationToken);
        await RequireStatusAsync(
            Endpoint(request.ManagerEndpoint, "readyz"),
            "ready",
            cancellationToken);
        using var openApi = await GetJsonAsync(
            Endpoint(request.ManagerEndpoint, "openapi.json"),
            authorization: null,
            cancellationToken);
        var topology = ValidateContract(openApi.RootElement);
        var components = new List<ObservedComponent>
        {
            new(
                topology == FspTopology.Lite ? "lite" : "manager",
                SupportedVersion,
                "healthy"),
        };

        if (topology == FspTopology.Enterprise)
        {
            if (request.CredentialKind == FspCredentialKind.None)
            {
                throw Failure(
                    DeploymentRegistrationFailure.IdentityRejected,
                    "deployment.credential_required",
                    "Enterprise Manager registration requires a credential reference.");
            }

            var secret = await credentialResolver.ResolveAsync(
                request.CredentialKind,
                request.CredentialReference,
                cancellationToken);
            var authorization = Authorization(
                request.CredentialKind,
                secret);
            using var agents = await GetJsonAsync(
                Endpoint(request.ManagerEndpoint, "agents"),
                authorization,
                cancellationToken);
            AddAgentVersions(agents.RootElement, components);
        }

        return new DeploymentProbeResult(topology, components);
    }

    private async Task<DeploymentObservationSnapshot> ObserveCoreAsync(
        DeploymentInventoryItem deployment,
        CancellationToken cancellationToken)
    {
        await RequireStatusAsync(
            Endpoint(deployment.ManagerEndpoint, "healthz"),
            "ok",
            cancellationToken);
        await RequireStatusAsync(
            Endpoint(deployment.ManagerEndpoint, "readyz"),
            "ready",
            cancellationToken);
        using var openApi = await GetJsonAsync(
            Endpoint(deployment.ManagerEndpoint, "openapi.json"),
            authorization: null,
            cancellationToken);
        var topology = ValidateContract(openApi.RootElement);
        if (!string.Equals(
                topology.ToString(),
                deployment.Topology,
                StringComparison.Ordinal))
        {
            throw ContractMismatch();
        }

        var values = new List<ObservedComponent>
        {
            new("health", "ok", "current"),
            new("readiness", "ready", "current"),
            new(
                topology == FspTopology.Lite ? "lite" : "manager",
                SupportedVersion,
                "current"),
        };
        if (topology == FspTopology.Enterprise)
        {
            if (!Enum.TryParse<FspCredentialKind>(
                    deployment.CredentialKind,
                    ignoreCase: false,
                    out var kind)
                || kind == FspCredentialKind.None)
            {
                throw Failure(
                    DeploymentRegistrationFailure.IdentityRejected,
                    "deployment.credential_required",
                    "Enterprise Manager observation requires a credential reference.");
            }

            var secret = await credentialResolver.ResolveAsync(
                kind,
                deployment.CredentialReference,
                cancellationToken);
            var authorization = Authorization(kind, secret);
            using var agents = await GetJsonAsync(
                Endpoint(deployment.ManagerEndpoint, "agents"),
                authorization,
                cancellationToken);
            AddFleetObservation(agents.RootElement, values);
            AddAgentVersions(agents.RootElement, values);
            using var queue = await GetJsonAsync(
                Endpoint(deployment.ManagerEndpoint, "control/work-queue"),
                authorization,
                cancellationToken);
            values.Add(
                new ObservedComponent(
                    "queue",
                    TopLevelArrayCounts(queue.RootElement),
                    "current"));
        }

        return new DeploymentObservationSnapshot(values);
    }

    private async Task RequireStatusAsync(
        Uri endpoint,
        string expectedStatus,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            endpoint,
            authorization: null,
            cancellationToken);
        if (!document.RootElement.TryGetProperty("status", out var status)
            || !string.Equals(
                status.GetString(),
                expectedStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                DeploymentRegistrationFailure.ContractMismatch,
                "deployment.health_invalid",
                "The FSP health or readiness response is invalid.");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(
        Uri endpoint,
        AuthenticationHeaderValue? authorization,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = authorization;
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden)
        {
            throw Failure(
                DeploymentRegistrationFailure.IdentityRejected,
                "deployment.identity_rejected",
                "The FSP endpoint rejected the referenced credential.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw Failure(
                DeploymentRegistrationFailure.ContractMismatch,
                "deployment.api_check_failed",
                "A required FSP API check did not succeed.");
        }

        await response.Content.LoadIntoBufferAsync(
            MaximumResponseBytes,
            cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private static FspTopology ValidateContract(JsonElement root)
    {
        var info = root.TryGetProperty("info", out var infoElement)
            ? infoElement
            : throw ContractMismatch();
        var title = GetRequiredString(info, "title");
        var version = info.TryGetProperty("x-fsp-release", out var release)
            ? release.GetString()
            : GetRequiredString(info, "version");
        if (!string.Equals(version, SupportedVersion, StringComparison.Ordinal))
        {
            throw Failure(
                DeploymentRegistrationFailure.IncompatibleVersion,
                "deployment.version_incompatible",
                $"FSP version {SupportedVersion} is required.");
        }

        var topology = title switch
        {
            "Fabric Shortcut Proxy (POC)" => FspTopology.Lite,
            "Fabric Shortcut Proxy: Manager" => FspTopology.Enterprise,
            _ => throw ContractMismatch(),
        };
        var paths = root.TryGetProperty("paths", out var pathsElement)
            ? pathsElement
            : throw ContractMismatch();
        var requiredPaths = topology == FspTopology.Lite
            ? new[] { "/", "/healthz", "/readyz" }
            : new[] { "/agents", "/healthz", "/readyz" };
        if (requiredPaths.Any(path => !paths.TryGetProperty(path, out _)))
        {
            throw ContractMismatch();
        }

        return topology;
    }

    private static AuthenticationHeaderValue Authorization(
        FspCredentialKind kind,
        string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw Failure(
                DeploymentRegistrationFailure.IdentityRejected,
                "deployment.credential_empty",
                "The referenced credential is empty.");
        }

        return kind switch
        {
            FspCredentialKind.Bearer => new AuthenticationHeaderValue(
                "Bearer",
                secret),
            FspCredentialKind.Basic => new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(secret))),
            _ => throw Failure(
                DeploymentRegistrationFailure.IdentityRejected,
                "deployment.credential_kind_invalid",
                "The credential kind is not supported by FSP Manager."),
        };
    }

    private static void AddAgentVersions(
        JsonElement root,
        List<ObservedComponent> components)
    {
        if (!root.TryGetProperty("agents", out var agents)
            || agents.ValueKind != JsonValueKind.Array)
        {
            throw ContractMismatch();
        }

        var index = 0;
        foreach (var agent in agents.EnumerateArray())
        {
            var version = FirstString(
                agent,
                "server_version",
                "version",
                "component_version");
            if (version is null)
            {
                index++;
                continue;
            }

            components.Add(
                new ObservedComponent(
                    FirstString(agent, "agent_id", "id", "name")
                        ?? $"agent-{index}",
                    version,
                    "observed"));
            index++;
        }
    }

    private static void AddFleetObservation(
        JsonElement root,
        List<ObservedComponent> values)
    {
        if (!root.TryGetProperty("agents", out var agents)
            || agents.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("dead", out var dead)
            || dead.ValueKind != JsonValueKind.Array)
        {
            throw ContractMismatch();
        }

        values.Add(
            new ObservedComponent(
                "fleet",
                $"active={agents.GetArrayLength()};dead={dead.GetArrayLength()}",
                "current"));
    }

    private static string TopLevelArrayCounts(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw ContractMismatch();
        }

        var counts = root.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.Array)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Take(10)
            .Select(
                property =>
                    $"{property.Name}={property.Value.GetArrayLength()}")
            .ToArray();
        return counts.Length == 0 ? "available" : string.Join(';', counts);
    }

    private static string? FirstString(
        JsonElement element,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static string GetRequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw ContractMismatch();

    private static Uri Endpoint(Uri managerEndpoint, string path)
    {
        var baseUri = managerEndpoint.AbsoluteUri.EndsWith('/')
            ? managerEndpoint
            : new Uri($"{managerEndpoint.AbsoluteUri}/");
        return new Uri(baseUri, path);
    }

    private static async Task<T> MapErrorsAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (DeploymentRegistrationException)
        {
            throw;
        }
        catch (AuthenticationException)
        {
            throw Failure(
                DeploymentRegistrationFailure.TlsValidationFailed,
                "deployment.tls_validation_failed",
                "TLS validation failed for the FSP endpoint.");
        }
        catch (HttpRequestException exception)
            when (exception.InnerException is AuthenticationException)
        {
            throw Failure(
                DeploymentRegistrationFailure.TlsValidationFailed,
                "deployment.tls_validation_failed",
                "TLS validation failed for the FSP endpoint.");
        }
        catch (HttpRequestException)
        {
            throw Failure(
                DeploymentRegistrationFailure.Unreachable,
                "deployment.endpoint_unreachable",
                "The FSP endpoint could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure(
                DeploymentRegistrationFailure.Unreachable,
                "deployment.endpoint_timeout",
                "The FSP endpoint did not respond before the timeout.");
        }
        catch (JsonException)
        {
            throw Failure(
                DeploymentRegistrationFailure.ContractMismatch,
                "deployment.contract_invalid",
                "The FSP endpoint returned an invalid contract response.");
        }
    }

    private static DeploymentRegistrationException ContractMismatch() =>
        Failure(
            DeploymentRegistrationFailure.ContractMismatch,
            "deployment.contract_mismatch",
            "The endpoint does not expose the required FSP 3.1.0 contract.");

    private static DeploymentRegistrationException Failure(
        DeploymentRegistrationFailure failure,
        string code,
        string message) =>
        new(failure, code, message);
}
