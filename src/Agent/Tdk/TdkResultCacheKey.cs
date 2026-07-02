using System.Security.Cryptography;
using System.Text;

namespace Edda.Agent.Tdk;

/// <summary>
/// Computes a stable cache key for a (rule × validator × code block) tuple. The key is a SHA-256 hash over
/// the rule id, the validator script, and the block's language + code, so any change to the rule, the
/// validator, or the code produces a different key (issue F13).
/// </summary>
public static class TdkResultCacheKey
{
    // NUL separator between fields so different field boundaries can never collide
    // (e.g. ("ab","c") vs ("a","bc")).
    private const char FieldSeparator = '\0';

    /// <summary>
    /// Computes the cache key for the given rule, validator script, and code block.
    /// </summary>
    /// <param name="ruleId">The rule's id.</param>
    /// <param name="validatorScript">The rule's validator script (its content is part of the identity).</param>
    /// <param name="blockLanguage">The code block's fence language.</param>
    /// <param name="blockCode">The code block's content.</param>
    /// <returns>An uppercase hex-encoded SHA-256 hash uniquely identifying the tuple.</returns>
    public static string Compute(string ruleId, string validatorScript, string blockLanguage, string blockCode)
    {
        var payload = string.Join(
            FieldSeparator, ruleId, validatorScript, blockLanguage, blockCode);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}
