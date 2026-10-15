using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Fsp.Orchestrator.Api.Hosting;

public interface IApplicationRestartScheduler
{
    void ScheduleRestart();
}

public sealed class ApplicationRestartScheduler(
    IHostApplicationLifetime applicationLifetime)
    : IApplicationRestartScheduler
{
    private int scheduled;

    public void ScheduleRestart()
    {
        if (Interlocked.Exchange(ref scheduled, 1) == 1)
        {
            return;
        }

        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Could not resolve the Orchestrator executable for restart.");
        var helperArgument = WindowsServiceHelpers.IsWindowsService()
            ? ProcessRestartBootstrap.RestartServiceArgument
            : ProcessRestartBootstrap.WaitForParentArgument;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add(helperArgument);
        start.ArgumentList.Add(
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        _ = Process.Start(start)
            ?? throw new InvalidOperationException(
                "Could not start the Orchestrator restart helper.");

        _ = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750));
                applicationLifetime.StopApplication();
            });
    }
}

public static class ProcessRestartBootstrap
{
    public const string WaitForParentArgument = "--wait-for-parent";
    public const string RestartServiceArgument = "--restart-windows-service";
    private const string ServiceName = "FSP Orchestrator";

    public static async Task<string[]?> PrepareAsync(string[] args)
    {
        if (args.Length != 2
            || (args[0] != WaitForParentArgument
                && args[0] != RestartServiceArgument)
            || !int.TryParse(args[1], out var parentProcessId))
        {
            return args;
        }

        await WaitForParentExitAsync(parentProcessId);
        if (args[0] == RestartServiceArgument)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "Windows Service restart is only supported on Windows.");
            }

            RestartWindowsService();
            return null;
        }

        return [];
    }

    [SupportedOSPlatform("windows")]
    private static void RestartWindowsService()
    {
        using var service = new ServiceController(ServiceName);
        service.Start();
        service.WaitForStatus(
            ServiceControllerStatus.Running,
            TimeSpan.FromSeconds(30));
    }

    private static async Task WaitForParentExitAsync(int parentProcessId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            await parent.WaitForExitAsync();
        }
        catch (ArgumentException)
        {
            // The parent exited before the helper attached.
        }
    }
}
