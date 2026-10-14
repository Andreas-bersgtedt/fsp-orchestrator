using Fsp.Orchestrator.Application;
using Fsp.Orchestrator.Domain;

namespace Fsp.Orchestrator.UnitTests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void CoreAssembliesHaveExpectedNames()
    {
        Assert.Equal("Orchestrator.Domain", typeof(DomainAssembly).Assembly.GetName().Name);
        Assert.Equal(
            "Orchestrator.Application",
            typeof(ApplicationAssembly).Assembly.GetName().Name);
    }
}
