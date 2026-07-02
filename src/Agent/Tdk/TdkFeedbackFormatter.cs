using System.Text;
using Edda.Core.Models;

namespace Edda.Agent.Tdk;

/// <summary>
/// Formats a list of TDK violations into a structured markdown feedback prompt
/// that is sent back to the LLM for self-correction.
/// </summary>
public static class TdkFeedbackFormatter
{
    /// <summary>
    /// Builds a markdown feedback message from the given violations.
    /// </summary>
    /// <param name="violations">
    /// One or more violations detected during TDK validation. Must not be empty.
    /// </param>
    /// <returns>
    /// A markdown-formatted string listing all violated rules and their details,
    /// suitable for appending to the conversation history as a User message.
    /// </returns>
    public static string Format(IReadOnlyList<TdkViolation> violations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("⚠️ **Code Review Required — Knowledge Base Violations**");
        sb.AppendLine();
        sb.AppendLine("The following issues were detected in your last response:");
        sb.AppendLine();

        foreach (var v in violations)
        {
            var location = v.Line is int line ? $" (line {line})" : string.Empty;
            sb.AppendLine($"**Rule: {v.RuleId}** [{v.Severity.ToUpperInvariant()}]{location}");
            sb.AppendLine($"- {v.Message}");
            if (!string.IsNullOrWhiteSpace(v.Suggestion))
            {
                sb.AppendLine($"- 💡 Suggestion: {v.Suggestion}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Please revise your response to address these violations.");
        return sb.ToString();
    }

    /// <summary>
    /// Builds a short markdown notice listing validators that could not be executed, so the caller
    /// knows the validation was incomplete rather than clean.
    /// </summary>
    /// <param name="errors">One or more engine errors. Must not be empty.</param>
    /// <returns>A markdown-formatted warning block.</returns>
    public static string FormatEngineErrors(IReadOnlyList<TdkEngineError> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("⚠️ **Some validators could not run** — the following rules were not checked:");
        foreach (var e in errors)
        {
            sb.AppendLine($"- Rule `{e.RuleId}`: {e.Reason}");
        }

        return sb.ToString();
    }
}
