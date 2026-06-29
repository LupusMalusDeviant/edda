namespace Edda.Security.OutputFilter;

/// <summary>
/// Redacts known secret patterns (API keys, tokens, credit cards, private keys, passwords) from
/// strings before they appear in any output, log, or response.
/// </summary>
public interface ISecretRedactor
{
    /// <summary>Replaces all detected secret patterns with safe placeholders. Never throws.</summary>
    /// <param name="input">The string to scan for secrets.</param>
    /// <returns>The input with detected secrets replaced.</returns>
    string Redact(string input);
}
