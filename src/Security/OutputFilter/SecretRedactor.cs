using System.Text.RegularExpressions;

namespace Edda.Security.OutputFilter;

/// <summary>
/// Redacts known secret patterns (API keys, tokens, credit cards, private keys, passwords)
/// from strings before they are included in any output, log, or response.
/// </summary>
public sealed class SecretRedactor : ISecretRedactor
{
    /// <summary>
    /// Compiled regex patterns and their respective replacement strings.
    /// Patterns are ordered from most specific to least specific to avoid partial matches.
    /// </summary>
    private static readonly (Regex Pattern, string Replacement)[] s_patterns =
    [
        (new(@"\bsk-ant-[a-zA-Z0-9\-]{20,}\b", RegexOptions.Compiled), "[API_KEY_ANT]"),
        (new(@"\bsk-[a-zA-Z0-9]{20,}\b", RegexOptions.Compiled), "[API_KEY_SK]"),
        (new(@"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|3[47][0-9]{13})\b", RegexOptions.Compiled), "[CREDIT_CARD]"),
        (new(@"\bBearer\s+[a-zA-Z0-9\-._~+/]+=*\b", RegexOptions.Compiled), "Bearer [TOKEN]"),
        (new(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled), "[AWS_KEY]"),
        (new(@"\bghp_[a-zA-Z0-9]{36}\b", RegexOptions.Compiled), "[GITHUB_TOKEN]"),
        (new(@"(?i)password\s*=\s*\S+", RegexOptions.Compiled), "password=[REDACTED]"),
        (new(@"-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----[\s\S]*?-----END (RSA |EC |OPENSSH )?PRIVATE KEY-----", RegexOptions.Compiled), "[PRIVATE_KEY]"),
    ];

    /// <summary>
    /// Scans the input string and replaces all detected secret patterns with safe placeholders.
    /// Never throws; returns the input unchanged if no patterns match.
    /// </summary>
    /// <param name="input">The string to scan for secrets.</param>
    /// <returns>The input with all detected secrets replaced by their respective placeholders.</returns>
    public string Redact(string input)
    {
        var result = input;
        foreach (var (pattern, replacement) in s_patterns)
        {
            result = pattern.Replace(result, replacement);
        }
        return result;
    }
}
