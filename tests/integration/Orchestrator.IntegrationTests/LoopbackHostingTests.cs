using Fsp.Orchestrator.Api.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Fsp.Orchestrator.IntegrationTests;

public sealed class LoopbackHostingTests
{
    [Theory]
    [InlineData("https://localhost:9443")]
    [InlineData("https://127.0.0.1:9443")]
    [InlineData("https://[::1]:9443")]
    [InlineData("https://localhost:9443;https://127.0.0.1:9443")]
    public void FixedLoopbackUrlsAreAccepted(string urls)
    {
        LoopbackHosting.ValidateUrlOverrides(urls);
    }

    [Theory]
    [InlineData("http://localhost:9443")]
    [InlineData("https://0.0.0.0:9443")]
    [InlineData("https://localhost:9444")]
    [InlineData("https://192.168.1.20:9443")]
    public void NonFixedOrNonLoopbackUrlsAreRejected(string urls)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => LoopbackHosting.ValidateUrlOverrides(urls));

        Assert.Contains(LoopbackHosting.Origin, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalhostCertificateValidationRejectsOtherNames()
    {
        using var valid = TestCertificate.Create("localhost");
        using var invalid = TestCertificate.Create("example.test");

        Assert.True(LocalhostCertificate.IsValidForLocalhost(valid));
        Assert.False(LocalhostCertificate.IsValidForLocalhost(invalid));
    }

    [Fact]
    public void LocalStateDirectoryPreservesFilesAcrossInitialization()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fsp-orchestrator-{Guid.NewGuid():N}");
        try
        {
            var first = LocalStateDirectory.Create(root, new TestHostEnvironment());
            var marker = Path.Combine(first.Path, "state.marker");
            File.WriteAllText(marker, "preserved");

            var second = LocalStateDirectory.Create(root, new TestHostEnvironment());

            Assert.Equal(first.Path, second.Path);
            Assert.Equal("preserved", File.ReadAllText(marker));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadinessTransitionsForStartupAndShutdown()
    {
        var readiness = new StartupReadiness();

        Assert.False(readiness.IsReady);
        readiness.MarkReady();
        Assert.True(readiness.IsReady);
        readiness.MarkStopping();
        Assert.False(readiness.IsReady);
    }

    [Fact]
    public async Task RestartHelperRemovesWaitArgumentsAfterParentExit()
    {
        var result = await ProcessRestartBootstrap.PrepareAsync(
            [
                ProcessRestartBootstrap.WaitForParentArgument,
                int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ]);

        Assert.Empty(result!);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Orchestrator.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
