using Fsp.Orchestrator.Application.Persistence;

namespace Fsp.Orchestrator.Application.Prerequisites;

public sealed class PrerequisiteEvidenceSanitizer(
    IPersistenceValueGuard valueGuard)
    : IPrerequisiteEvidenceSanitizer
{
    private const int MaximumEvidenceItems = 20;
    private const int MaximumValueLength = 2_048;

    public IReadOnlyDictionary<string, string> Sanitize(
        IReadOnlyDictionary<string, string> evidence)
    {
        var sanitized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in evidence
                     .OrderBy(item => item.Key, StringComparer.Ordinal)
                     .Take(MaximumEvidenceItems))
        {
            var key = item.Key.Length > 100
                ? item.Key[..100]
                : item.Key;
            var value = item.Value.Length > MaximumValueLength
                ? item.Value[..MaximumValueLength]
                : item.Value;
            try
            {
                valueGuard.EnsureSafe(key, value);
                sanitized[key] = value;
            }
            catch (UnsafePersistenceValueException)
            {
                sanitized[key] = "[REDACTED]";
            }
        }

        return sanitized;
    }
}
