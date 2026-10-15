using System.Security.Cryptography;
using System.Text;

namespace Fsp.Orchestrator.Application.Prerequisites;

public static class PrerequisiteFingerprint
{
    public static string Compute(
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyList<string>? keys = null)
    {
        var selectedKeys = keys is null or { Count: 0 }
            ? inputs.Keys
            : keys;
        var canonical = string.Join(
            '\n',
            selectedKeys
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(
                    key => $"{key}={inputs.GetValueOrDefault(key, string.Empty)}"));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
