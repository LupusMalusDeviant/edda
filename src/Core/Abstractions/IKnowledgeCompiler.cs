using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Extracts structured knowledge rules from unstructured text using LLM synthesis
/// and persists them in the Agent Knowledge Graph.
/// </summary>
/// <remarks>
/// Supported source types: free text, web page content, learnings.md, memory.md.
/// Rules are user-scoped (OwnerId set from userId parameter).
/// </remarks>
public interface IKnowledgeCompiler
{
    /// <summary>
    /// Analyzes the provided text and extracts <see cref="KnowledgeRule"/> candidates via LLM.
    /// If <paramref name="preview"/> is false, all valid rules are persisted in the AKG immediately.
    /// Existing rules with identical IDs are skipped (deduplication by ID).
    /// </summary>
    /// <param name="text">Unstructured input text (web content, chat log, code, free text).</param>
    /// <param name="domainHint">Optional domain name to guide the LLM extraction (e.g. "csharp", "security").</param>
    /// <param name="sourceType">Origin label stored on each created rule (e.g. "web", "learnings", "text").</param>
    /// <param name="sourceUrl">Optional URL attribution stored on each created rule.</param>
    /// <param name="userId">Owner ID set on all extracted rules. Must come from the authenticated context.</param>
    /// <param name="preview">
    /// When true, rules are returned but NOT saved. Useful for reviewing candidates before committing.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="KnowledgeCompilationResult"/> describing extracted, saved, and skipped rules.</returns>
    Task<KnowledgeCompilationResult> CompileAsync(
        string text,
        string? domainHint,
        string sourceType,
        string? sourceUrl,
        string userId,
        bool preview,
        CancellationToken ct);
}
