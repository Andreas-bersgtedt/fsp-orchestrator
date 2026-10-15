using System.Text.RegularExpressions;

namespace Fsp.Orchestrator.Application.Persistence;

public sealed partial class PersistenceValueGuard : IPersistenceValueGuard
{
    public void EnsureSafe(params string?[] values)
    {
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (SensitiveNamePattern().IsMatch(value!)
                || BearerPattern().IsMatch(value!)
                || JwtPattern().IsMatch(value!)
                || SecretConnectionStringPattern().IsMatch(value!))
            {
                throw new UnsafePersistenceValueException(
                    "The value was rejected because it may contain a token or secret.");
            }
        }
    }

    [GeneratedRegex(
        """(?ix)(client[_-]?secret|secret[_-]?access[_-]?key|authorization["'\s:=]|refresh[_-]?token|access[_-]?token)""")]
    private static partial Regex SensitiveNamePattern();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{16,}")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b")]
    private static partial Regex JwtPattern();

    [GeneratedRegex(
        @"(?i)(Password|AccountKey|SharedAccessSignature)\s*=\s*[^;\s]+")]
    private static partial Regex SecretConnectionStringPattern();
}

public sealed class UnsafePersistenceValueException(string message)
    : InvalidOperationException(message);

public sealed class PersistenceConcurrencyException : InvalidOperationException
{
    public PersistenceConcurrencyException(string message)
        : base(message)
    {
    }

    public PersistenceConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class InvalidJobTransitionException(string message)
    : InvalidOperationException(message);
