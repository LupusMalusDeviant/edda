namespace Edda.Core.Abstractions;

/// <summary>
/// Manages AKG knowledge rules for custom tools.
/// Automatically creates or deletes tool documentation in the "custom-tools" domain
/// when users create or remove custom tools via <c>manage_custom_tools</c>.
/// </summary>
public interface IToolKnowledgeService
{
    /// <summary>
    /// Creates or updates an AKG knowledge rule for a custom tool.
    /// The rule is user-scoped (<c>ownerId = userId</c>) in the "custom-tools" domain.
    /// </summary>
    /// <param name="toolName">Custom tool name (will be sanitised to kebab-case for the rule ID).</param>
    /// <param name="description">Human-readable description used in the rule body.</param>
    /// <param name="tags">User-defined tags added alongside system tags.</param>
    /// <param name="userId">Owning user ID — the rule is only visible to this user.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertCustomToolRuleAsync(
        string toolName,
        string description,
        IReadOnlyList<string> tags,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes the AKG knowledge rule for a custom tool.
    /// No-op if the rule does not exist.
    /// </summary>
    /// <param name="toolName">Custom tool name (used to derive the rule ID).</param>
    /// <param name="userId">Owning user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteCustomToolRuleAsync(
        string toolName,
        string userId,
        CancellationToken ct = default);
}
