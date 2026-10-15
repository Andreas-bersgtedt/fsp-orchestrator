using System.Reflection;
using System.Text.Json;
using Fsp.Orchestrator.Application.Deployments;

namespace Fsp.Orchestrator.Infrastructure.Deployments;

public sealed class ReleaseManifestCompatibilityPolicy
    : IReleaseCompatibilityPolicy
{
    public ReleaseManifestCompatibilityPolicy()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(
            name => name.EndsWith(
                "release-manifest.json",
                StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                "The embedded FSP release manifest is unavailable.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var releaseVersion = RequiredString(root, "fspVersion");
        var liteVersion = RequiredString(
            root.GetProperty("packages").GetProperty("lite"),
            "version");
        var managerImage = root.GetProperty("images")
            .EnumerateArray()
            .Single(
                image => string.Equals(
                    RequiredString(image, "component"),
                    "manager",
                    StringComparison.Ordinal));
        var managerReference = RequiredString(managerImage, "reference");
        var separator = managerReference.LastIndexOf(':');
        if (separator < 0 || separator == managerReference.Length - 1)
        {
            throw new InvalidOperationException(
                "The Manager image reference has no version tag.");
        }

        Requirements = new ReleaseCompatibilityRequirements(
            releaseVersion,
            liteVersion,
            managerReference[(separator + 1)..],
            root.GetProperty("componentsMoveTogether").GetBoolean());
    }

    public ReleaseCompatibilityRequirements Requirements { get; }

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException(
                $"The release manifest property '{property}' is invalid.");
        }

        return value.GetString()!;
    }
}
