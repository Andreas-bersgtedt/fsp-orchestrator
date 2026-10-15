using System.Net.Sockets;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Application.Persistence;
using Fsp.Orchestrator.Application.Prerequisites;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Fsp.Orchestrator.Api.Hosting;

public sealed class LocalPrerequisiteProbe(
    HostingRuntime hostingRuntime,
    EntraAuthenticationConfiguration entra,
    IDatabaseMaintenance databaseMaintenance,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider)
    : ILocalPrerequisiteProbe
{
    public LocalHostSnapshot GetHost() =>
        new(OperatingSystem.IsWindows(), WindowsServiceHelpers.IsWindowsService());

    public async Task<bool> IsLoopbackListenerAvailableAsync(
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(
                "127.0.0.1",
                LoopbackHosting.HttpsPort,
                cancellationToken);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public LocalCertificateSnapshot GetCertificate() =>
        new(
            hostingRuntime.CertificateSubject,
            hostingRuntime.CertificateExpiresAt);

    public async Task<LocalDatabaseSnapshot> GetDatabaseAsync(
        CancellationToken cancellationToken)
    {
        var diagnostics = await databaseMaintenance.GetDiagnosticsAsync(
            cancellationToken);
        var root = Path.GetPathRoot(hostingRuntime.DataDirectory)
            ?? throw new InvalidOperationException(
                "The Orchestrator data volume could not be resolved.");
        var drive = new DriveInfo(root);
        return new LocalDatabaseSnapshot(
            diagnostics.Integrity,
            diagnostics.Migration,
            diagnostics.PendingMigrationCount,
            drive.AvailableFreeSpace);
    }

    public async Task<LocalClockSnapshot> GetClockAsync(
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration");
        using var response = await httpClientFactory.CreateClient(
                nameof(LocalPrerequisiteProbe))
            .SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        response.EnsureSuccessStatusCode();
        var serverTime = response.Headers.Date
            ?? throw new InvalidOperationException(
                "The trusted time response did not include a Date header.");
        return new LocalClockSnapshot(
            "login.microsoftonline.com",
            timeProvider.GetUtcNow() - serverTime);
    }

    public LocalEntraSnapshot GetEntra() =>
        new(
            entra.TenantId,
            entra.ClientId,
            entra.SpaClientId,
            entra.RedirectUri,
            entra.ScopeName,
            entra.ApiScope);
}
