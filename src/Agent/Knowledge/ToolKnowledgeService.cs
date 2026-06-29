using System.Text;
using System.Text.RegularExpressions;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Knowledge;

/// <summary>
/// Creates and deletes AKG knowledge rules for user-created custom tools.
/// Each custom tool gets a <c>Guideline</c> rule in the <c>custom-tools</c> domain,
/// scoped to the owning user.
/// </summary>
internal sealed partial class ToolKnowledgeService : IToolKnowledgeService
{
    /// <summary>Prefix for all custom tool rule IDs.</summary>
    internal const string RuleIdPrefix = "custom-tool-";

    /// <summary>The AKG domain used for custom tool documentation.</summary>
    internal const string CustomToolsDomain = "custom-tools";

    private readonly IKnowledgeGraph _graph;
    private readonly ILogger<ToolKnowledgeService> _logger;

    /// <summary>
    /// Initializes a new <see cref="ToolKnowledgeService"/>.
    /// </summary>
    /// <param name="graph">Knowledge graph for rule persistence.</param>
    /// <param name="logger">Structured logger.</param>
    public ToolKnowledgeService(
        IKnowledgeGraph graph,
        ILogger<ToolKnowledgeService> logger)
    {
        _graph = graph;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task UpsertCustomToolRuleAsync(
        string toolName,
        string description,
        IReadOnlyList<string> tags,
        string userId,
        CancellationToken ct = default)
    {
        var kebabName = ToKebabCase(toolName);
        var ruleId = $"{RuleIdPrefix}{kebabName}";

        var systemTags = new List<string> { "tool", "custom-tool", kebabName };
        foreach (var tag in tags)
        {
            var normalized = tag.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(normalized) && !systemTags.Contains(normalized))
                systemTags.Add(normalized);
        }

        var concepts = ExtractConcepts(toolName, description);
        var body = BuildRuleBody(toolName, description);

        var rule = new KnowledgeRule
        {
            Id = ruleId,
            Type = "Guideline",
            Domain = CustomToolsDomain,
            Priority = RulePriority.Medium,
            Confidence = 1.0,
            Tags = systemTags,
            Author = userId,
            Body = body,
            OwnerId = userId,
            SourceType = "manual",
            WhenRelevant = new WhenRelevant
            {
                DetectedConcepts = concepts,
            },
        };

        await _graph.UpsertRuleAsync(rule, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Custom tool AKG rule upserted: {RuleId} for user {UserId} | {Component}",
            ruleId, userId, "ToolKnowledge");
    }

    /// <inheritdoc />
    public async Task DeleteCustomToolRuleAsync(
        string toolName,
        string userId,
        CancellationToken ct = default)
    {
        var kebabName = ToKebabCase(toolName);
        var ruleId = $"{RuleIdPrefix}{kebabName}";

        try
        {
            await _graph.DeleteRuleAsync(ruleId, userId, isAdmin: false, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Custom tool AKG rule deleted: {RuleId} for user {UserId} | {Component}",
                ruleId, userId, "ToolKnowledge");
        }
        catch (Exception ex)
        {
            // Graceful: if rule doesn't exist or delete fails, just log a warning.
            _logger.LogWarning(ex,
                "Failed to delete custom tool AKG rule {RuleId} for user {UserId} | {Component}",
                ruleId, userId, "ToolKnowledge");
        }
    }

    /// <summary>
    /// Converts a tool name to kebab-case for use as a rule ID component.
    /// </summary>
    internal static string ToKebabCase(string name)
    {
        // Replace spaces, underscores, dots with hyphens
        var result = KebabSeparatorRegex().Replace(name.Trim(), "-");
        // Remove anything that's not alphanumeric or hyphen
        result = NonKebabCharRegex().Replace(result, string.Empty);
        // Collapse multiple hyphens
        result = MultiHyphenRegex().Replace(result, "-");
        return result.Trim('-').ToLowerInvariant();
    }

    /// <summary>
    /// Extracts concept keywords from the tool name and description.
    /// </summary>
    internal static IReadOnlyList<string> ExtractConcepts(string toolName, string description)
    {
        var concepts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Split tool name by separators
        foreach (var part in KebabSeparatorRegex().Split(toolName.Trim()))
        {
            var clean = NonKebabCharRegex().Replace(part, string.Empty).Trim().ToLowerInvariant();
            if (clean.Length >= 3)
                concepts.Add(clean);
        }

        // Extract significant words from description (4+ chars, no stop words)
        var words = WordSplitRegex().Split(description);
        foreach (var word in words)
        {
            var lower = word.ToLowerInvariant();
            if (lower.Length >= 4 && !StopWords.Contains(lower))
                concepts.Add(lower);
        }

        // Add "custom-tool" as a concept
        concepts.Add("custom-tool");

        return concepts.ToList();
    }

    private static string BuildRuleBody(string toolName, string description)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Custom Tool: {toolName}");
        sb.AppendLine();
        sb.AppendLine("### Beschreibung");
        sb.AppendLine();
        sb.AppendLine(description);
        sb.AppendLine();
        sb.AppendLine("### Hinweise");
        sb.AppendLine();
        sb.AppendLine("- Dieses Tool ist ein benutzerdefiniertes Python-Skript");
        sb.AppendLine("- Ausführung erfolgt in einer isolierten Sandbox");
        sb.AppendLine("- Input wird als JSON über stdin übergeben, Output über stdout");
        return sb.ToString();
    }

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "that", "this", "with", "from", "into", "will",
        "have", "been", "does", "each", "than", "also", "more", "some",
        "der", "die", "das", "und", "für", "mit", "von", "aus", "wird",
        "eine", "einer", "einem", "einen", "sind", "wird", "kann", "nach",
        "über", "oder", "aber", "wenn", "dann", "auch", "noch", "nicht",
    ];

    [GeneratedRegex(@"[\s_\.\-]+")]
    private static partial Regex KebabSeparatorRegex();

    [GeneratedRegex(@"[^a-zA-Z0-9\-]")]
    private static partial Regex NonKebabCharRegex();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultiHyphenRegex();

    [GeneratedRegex(@"\W+")]
    private static partial Regex WordSplitRegex();
}
