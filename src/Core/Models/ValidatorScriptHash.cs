using System.Security.Cryptography;
using System.Text;

namespace Edda.Core.Models;

/// <summary>
/// Computes the SHA-256 hash of a TDK validator script (F7). The hash is persisted on the rule for
/// traceability and used by the confidence store to reset a rule's outcome window when its validator
/// script changes — old outcomes measured the old code.
/// </summary>
public static class ValidatorScriptHash
{
    /// <summary>
    /// Returns the lowercase-hex SHA-256 of <paramref name="script"/>, or <see langword="null"/> when the
    /// script is null or empty.
    /// </summary>
    /// <param name="script">The validator script, or null.</param>
    /// <returns>The lowercase-hex SHA-256 digest, or null.</returns>
    public static string? Compute(string? script)
    {
        if (string.IsNullOrEmpty(script))
            return null;

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(script));
        return Convert.ToHexStringLower(digest);
    }
}
