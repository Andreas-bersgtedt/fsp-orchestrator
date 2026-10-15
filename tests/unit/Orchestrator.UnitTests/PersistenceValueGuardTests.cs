using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.UnitTests;

public sealed class PersistenceValueGuardTests
{
    private readonly PersistenceValueGuard guard = new();

    [Theory]
    [InlineData("Authorization: Bearer abcdefghijklmnop")]
    [InlineData("Bearer abcdefghijklmnop")]
    [InlineData("client_secret=do-not-store")]
    [InlineData("Password=do-not-store;Server=localhost")]
    [InlineData("eyJabcdefgh.ijklmnop.qrstuvwx")]
    public void SecretLikeValuesAreRejected(string value)
    {
        Assert.Throws<UnsafePersistenceValueException>(
            () => guard.EnsureSafe(value));
    }

    [Theory]
    [InlineData("deployment.registered")]
    [InlineData("https://manager.example.test")]
    [InlineData("safe provider output")]
    public void SafeMetadataIsAccepted(string value)
    {
        guard.EnsureSafe(value);
    }
}
