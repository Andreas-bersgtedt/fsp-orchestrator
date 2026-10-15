using System.Text.Json;
using Fsp.Orchestrator.Application.Identity;

namespace Fsp.Orchestrator.UnitTests;

public sealed class EntraRegistrationContractTests
{
    [Fact]
    public void CheckedInContractMatchesValidatorConstants()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot.FullName,
                    "contracts",
                    "orchestrator",
                    "entra-registration.json")));
        var root = document.RootElement;

        Assert.Equal(
            EntraRegistrationContract.SingleTenantAudience,
            root.GetProperty("signInAudience").GetString());
        Assert.Equal(
            [EntraRegistrationContract.RedirectUri],
            root.GetProperty("spa")
                .GetProperty("redirectUris")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            EntraRegistrationContract.ScopeName,
            root.GetProperty("api")
                .GetProperty("delegatedScope")
                .GetProperty("value")
                .GetString());
        Assert.Equal(
            EntraRegistrationContract.Roles,
            root.GetProperty("api")
                .GetProperty("roles")
                .EnumerateArray()
                .Select(role => role.GetProperty("value").GetString()));
        Assert.False(root.GetProperty("spa").GetProperty("credentialsAllowed").GetBoolean());
        Assert.False(root.GetProperty("api").GetProperty("credentialsAllowed").GetBoolean());
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
