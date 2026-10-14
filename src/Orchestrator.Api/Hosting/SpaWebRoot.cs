namespace Fsp.Orchestrator.Api.Hosting;

public static class SpaWebRoot
{
    public static string Resolve()
    {
        var outputWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (File.Exists(Path.Combine(outputWebRoot, "index.html")))
        {
            return outputWebRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Fsp.Orchestrator.slnx")))
            {
                var developmentWebRoot = Path.Combine(
                    directory.FullName,
                    "src",
                    "Orchestrator.Web",
                    "dist");
                if (File.Exists(Path.Combine(developmentWebRoot, "index.html")))
                {
                    return developmentWebRoot;
                }

                break;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "The built Orchestrator SPA was not found. Run the solution build before startup.");
    }
}
