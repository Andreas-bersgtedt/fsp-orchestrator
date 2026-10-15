using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Fsp.Orchestrator.Api.Hosting;

public static class LoopbackHosting
{
    public const int HttpsPort = 9443;
    public const string Origin = "https://localhost:9443";

    public static HostingRuntime Configure(WebApplicationBuilder builder)
    {
        ValidateUrlOverrides(builder.Configuration["urls"]);

        var stateDirectory = LocalStateDirectory.Create(
            builder.Configuration["Orchestrator:DataDirectory"],
            builder.Environment);
        builder.Configuration.AddJsonFile(
            Path.Combine(
                stateDirectory.Path,
                IdentityConfigurationStore.FileName),
            optional: true,
            reloadOnChange: false);
        var certificate = LocalhostCertificate.Load(
            builder.Configuration["Orchestrator:Certificate:Thumbprint"],
            builder.Configuration["Orchestrator:Certificate:StoreLocation"]);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.ListenLocalhost(
                HttpsPort,
                listenOptions => listenOptions.UseHttps(certificate));
        });

        return new HostingRuntime(Origin, stateDirectory.Path, certificate.NotAfter);
    }

    public static void ValidateUrlOverrides(string? configuredUrls)
    {
        if (string.IsNullOrWhiteSpace(configuredUrls))
        {
            return;
        }

        foreach (var configuredUrl in configuredUrls.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !IsLoopbackHost(uri.Host)
                || uri.Port != HttpsPort)
            {
                throw new InvalidOperationException(
                    $"FSP Orchestrator only accepts the fixed loopback origin {Origin}. "
                    + $"Rejected listener URL '{configuredUrl}'.");
            }
        }
    }

    private static bool IsLoopbackHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
    }
}

public sealed record HostingRuntime(
    string Origin,
    string DataDirectory,
    DateTimeOffset CertificateExpiresAt);
