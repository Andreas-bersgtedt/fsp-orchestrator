using System.Xml.Linq;
using Fsp.Orchestrator.Domain;

namespace Fsp.Orchestrator.ArchitectureTests;

public sealed class ProjectBoundaryTests
{
    private static readonly DirectoryInfo RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void DomainHasNoExternalPackageReferences()
    {
        var project = LoadProject("src", "Orchestrator.Domain", "Orchestrator.Domain.csproj");

        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("ProjectReference"));
    }

    [Fact]
    public void ProjectReferencesFollowDependencyDirection()
    {
        AssertReferences(
            ("src", "Orchestrator.Application", "Orchestrator.Application.csproj"),
            "src/Orchestrator.Domain/Orchestrator.Domain.csproj");
        AssertReferences(
            ("src", "Orchestrator.Infrastructure", "Orchestrator.Infrastructure.csproj"),
            "src/Orchestrator.Application/Orchestrator.Application.csproj");
        AssertReferences(
            ("src", "Orchestrator.Api", "Orchestrator.Api.csproj"),
            "src/Orchestrator.Application/Orchestrator.Application.csproj",
            "src/Orchestrator.Infrastructure/Orchestrator.Infrastructure.csproj");
    }

    [Fact]
    public void DomainDoesNotReferenceInfrastructureFrameworks()
    {
        var references = typeof(DomainAssembly).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();

        Assert.DoesNotContain(references, name => name!.StartsWith("Azure.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name == "System.Net.Http");
        Assert.DoesNotContain(
            references,
            name => name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name == "Microsoft.Data.Sqlite");
    }

    [Fact]
    public void ProductionSourceDoesNotReferenceTestFixturePath()
    {
        foreach (var sourceFile in ProductionSourceFiles())
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain("FSP_TEST_SOURCE", source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("ClientSecret")]
    [InlineData("client_secret")]
    [InlineData("client-secret")]
    public void ProductionSourceDoesNotDefineSpaClientSecrets(string forbiddenTerm)
    {
        foreach (var sourceFile in ProductionSourceFiles())
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain(
                forbiddenTerm,
                source,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<string> ProductionSourceFiles()
    {
        var sourceRoot = Path.Combine(RepositoryRoot.FullName, "src");
        var dotnetSourceFiles = Directory.EnumerateFiles(
                sourceRoot,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !HasDirectory(path, "bin") && !HasDirectory(path, "obj"));
        var webSourceRoot = Path.Combine(sourceRoot, "Orchestrator.Web", "src");
        var webSourceFiles = Directory.EnumerateFiles(
                webSourceRoot,
                "*.*",
                SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase));
        return dotnetSourceFiles.Concat(webSourceFiles);
    }

    private static bool HasDirectory(string path, string directoryName)
    {
        return path.Split(Path.DirectorySeparatorChar)
            .Contains(directoryName, StringComparer.OrdinalIgnoreCase);
    }

    private static XDocument LoadProject(params string[] segments)
    {
        return XDocument.Load(Path.Combine([RepositoryRoot.FullName, .. segments]));
    }

    private static void AssertReferences(
        (string Root, string Project, string File) projectPath,
        params string[] expectedReferences)
    {
        var project = LoadProject(projectPath.Root, projectPath.Project, projectPath.File);
        var projectDirectory = Path.Combine(
            RepositoryRoot.FullName,
            projectPath.Root,
            projectPath.Project);
        var actualReferences = project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value)
            .Select(reference => Path.GetFullPath(reference, projectDirectory))
            .Select(reference => Path.GetRelativePath(RepositoryRoot.FullName, reference))
            .Select(reference => reference.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expectedReferences.Order(StringComparer.Ordinal),
            actualReferences);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Fsp.Orchestrator.slnx")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Fsp.Orchestrator.slnx.");
    }
}
