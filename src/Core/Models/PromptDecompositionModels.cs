namespace Edda.Core.Models;

/// <summary>
/// A self-contained prompt package for one Claude Code agent.
/// Contains all context, instructions, cross-references, and prototype references
/// the agent needs to complete its task independently.
/// </summary>
/// <param name="TaskId">Unique task identifier from the DevPlan.</param>
/// <param name="AgentRole">Role: "frontend", "backend", "database", etc.</param>
/// <param name="ContextMd">CONTEXT.md — AKG rules, architecture, coding standards.</param>
/// <param name="TaskMd">TASK.md — Concrete work instructions and acceptance criteria.</param>
/// <param name="ApiContractMd">API-CONTRACT.md — Cross-agent interface definitions. Null if no cross-references.</param>
/// <param name="PrototypeRefMd">PROTOTYPE.md — Relevant prototype pages and design decisions. Null if no prototype.</param>
/// <param name="FileScope">Directories/files this agent should focus on.</param>
/// <param name="AkgDomains">AKG domain names relevant to this task.</param>
public sealed record AgentPromptPackage(
    string TaskId,
    string AgentRole,
    string ContextMd,
    string TaskMd,
    string? ApiContractMd,
    string? PrototypeRefMd,
    IReadOnlyList<string> FileScope,
    IReadOnlyList<string> AkgDomains);

/// <summary>
/// Report on how well the prompt packages cover the Pflichtenheft content.
/// </summary>
/// <param name="FullyCovered">True if every Pflichtenheft chapter is referenced by at least one agent.</param>
/// <param name="CoveredChapters">List of chapter headings covered by agent prompts.</param>
/// <param name="UncoveredChapters">List of chapter headings not referenced by any agent.</param>
/// <param name="Warnings">Non-critical issues: overlapping file scopes, redundant assignments, etc.</param>
public sealed record PromptCoverageReport(
    bool FullyCovered,
    IReadOnlyList<string> CoveredChapters,
    IReadOnlyList<string> UncoveredChapters,
    IReadOnlyList<string> Warnings);
