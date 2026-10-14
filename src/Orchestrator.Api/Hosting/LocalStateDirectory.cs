namespace Fsp.Orchestrator.Api.Hosting;

public sealed class LocalStateDirectory
{
    private LocalStateDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static LocalStateDirectory Create(
        string? configuredPath,
        IHostEnvironment environment)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultPath(environment)
            : System.IO.Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(configuredPath));

        Directory.CreateDirectory(path);
        return new LocalStateDirectory(path);
    }

    private static string DefaultPath(IHostEnvironment environment)
    {
        var specialFolder = environment.IsDevelopment()
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.CommonApplicationData;
        var root = Environment.GetFolderPath(specialFolder);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                $"Could not resolve {specialFolder} for the Orchestrator data directory.");
        }

        return environment.IsDevelopment()
            ? System.IO.Path.Combine(root, "FspOrchestrator", "Development")
            : System.IO.Path.Combine(root, "FspOrchestrator");
    }
}
