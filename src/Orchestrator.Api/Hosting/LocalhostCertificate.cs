using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Fsp.Orchestrator.Api.Hosting;

public static class LocalhostCertificate
{
    public static X509Certificate2 Load(
        string? configuredThumbprint,
        string? configuredStoreLocation)
    {
        var storeLocation = ParseStoreLocation(configuredStoreLocation);
        using var store = new X509Store(StoreName.My, storeLocation);
        store.Open(OpenFlags.ReadOnly);

        var candidates = string.IsNullOrWhiteSpace(configuredThumbprint)
            ? store.Certificates.Find(
                X509FindType.FindBySubjectName,
                "localhost",
                validOnly: false)
            : store.Certificates.Find(
                X509FindType.FindByThumbprint,
                NormalizeThumbprint(configuredThumbprint),
                validOnly: false);

        var certificate = candidates
            .Where(candidate => candidate.HasPrivateKey)
            .OrderByDescending(candidate => candidate.NotAfter)
            .FirstOrDefault(candidate => IsValidForLocalhost(candidate));

        if (certificate is not null)
        {
            return certificate;
        }

        var selector = string.IsNullOrWhiteSpace(configuredThumbprint)
            ? "a valid localhost certificate"
            : $"certificate '{configuredThumbprint}'";
        throw new InvalidOperationException(
            $"Could not load {selector} with a private key from "
            + $"{storeLocation}\\{StoreName.My}. For development, run "
            + "'dotnet dev-certs https --trust'. For service hosting, install the "
            + "certificate for the service account and configure its thumbprint.");
    }

    public static bool IsValidForLocalhost(X509Certificate2 certificate)
    {
        var now = DateTimeOffset.UtcNow;
        return certificate.HasPrivateKey
            && now >= certificate.NotBefore
            && now < certificate.NotAfter
            && SslPolicyErrors.None == ValidateName(certificate);
    }

    private static SslPolicyErrors ValidateName(X509Certificate2 certificate)
    {
        var dnsName = certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false);
        return string.Equals(dnsName, "localhost", StringComparison.OrdinalIgnoreCase)
            ? SslPolicyErrors.None
            : SslPolicyErrors.RemoteCertificateNameMismatch;
    }

    private static StoreLocation ParseStoreLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return StoreLocation.CurrentUser;
        }

        if (Enum.TryParse<StoreLocation>(value, ignoreCase: true, out var location))
        {
            return location;
        }

        throw new InvalidOperationException(
            $"Unsupported certificate store location '{value}'. "
            + "Use CurrentUser or LocalMachine.");
    }

    private static string NormalizeThumbprint(string thumbprint)
    {
        return string.Concat(thumbprint.Where(character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
    }
}
