namespace Edda.Core.Models;

/// <summary>
/// Represents an ASPS.ai project with its metadata and status flags.
/// </summary>
public sealed record AspsProject(
    int Id,
    string Slug,
    string Name,
    string? Description,
    string Stage,
    string ExecutionMode,
    bool HasLastenheft,
    bool HasPflichtenheft,
    bool HasPrototype,
    IReadOnlyList<string> BuildTechStack,
    string? PrototypeShareUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents a Lastenheft (requirements specification) from ASPS.ai.
/// Contains the full Markdown content following the 14-chapter structure.
/// </summary>
public sealed record AspsLastenheft(
    int Id,
    string Content,
    int Version,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AspsLastenheftSection> Sections);

/// <summary>
/// Represents a Pflichtenheft (technical specification) from ASPS.ai.
/// Includes optional C4 architecture diagram as Mermaid code.
/// </summary>
public sealed record AspsPflichtenheft(
    int Id,
    string Content,
    int Version,
    string Status,
    string? C4Mermaid,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents a task from the ASPS.ai project management module.
/// Tasks are extracted from the Pflichtenheft Chapter 12.
/// </summary>
public sealed record AspsTask(
    int Id,
    string TaskId,
    string Title,
    string? Ziel,
    string? Nutzen,
    string? Ergebnis,
    string? Description,
    string? Modul,
    string Status,
    decimal? HoursEstimate,
    string? DependsOnTaskId,
    string? AssignedAgent,
    int? ImpactPriority,
    int? EffortPriority,
    int? SortOrder,
    int? EpicId,
    int? ModuleId);

/// <summary>
/// Represents an epic grouping multiple tasks in the ASPS.ai project.
/// </summary>
public sealed record AspsEpic(
    int Id,
    string Title,
    string? Description,
    string? AgentType,
    int? ModuleId,
    string Status,
    IReadOnlyList<AspsTask> Tasks);

/// <summary>
/// Collection of all epics and standalone tasks for a project.
/// </summary>
public sealed record AspsTaskCollection(
    IReadOnlyList<AspsEpic> Epics,
    IReadOnlyList<AspsTask> Tasks);

/// <summary>
/// Represents an offer (cost estimation) document from ASPS.ai.
/// </summary>
public sealed record AspsOffer(
    int Id,
    string Content,
    string Status,
    DateTimeOffset? GeneratedAt,
    IReadOnlyList<AspsOfferScreenshot> Screenshots);

/// <summary>
/// Section of a Lastenheft with approval status.
/// </summary>
public sealed record AspsLastenheftSection(
    string Key,
    string Title,
    string Status,
    DateTimeOffset? ApprovedAt);

/// <summary>
/// Screenshot reference in an ASPS offer document.
/// </summary>
public sealed record AspsOfferScreenshot(
    string Name,
    string Url);

/// <summary>
/// Version entry for a Lastenheft or Pflichtenheft.
/// </summary>
public sealed record AspsDocumentVersion(
    int Id,
    int Version,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents a module grouping epics and tasks in the ASPS.ai project.
/// </summary>
public sealed record AspsModule(
    int Id,
    string Name,
    string Slug,
    int Order,
    string Status,
    IReadOnlyList<AspsEpic> Epics,
    IReadOnlyList<AspsTask> Tasks);

/// <summary>
/// Represents a project milestone with due date and completion status.
/// </summary>
public sealed record AspsMilestone(
    int Id,
    string Title,
    string? DueDate,
    string Status);

/// <summary>
/// Chat history for an ASPS.ai project including messages and file attachments.
/// </summary>
public sealed record AspsChatHistory(
    IReadOnlyList<AspsChatMessage> Messages,
    bool HasSession,
    IReadOnlyList<AspsChatAttachment> Attachments);

/// <summary>
/// Single chat message in an ASPS.ai project conversation.
/// </summary>
public sealed record AspsChatMessage(
    int Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

/// <summary>
/// File attachment reference in an ASPS.ai chat.
/// </summary>
public sealed record AspsChatAttachment(
    string Id,
    string Filename);

/// <summary>
/// Discovery session data with follow-up questions from ASPS.ai.
/// </summary>
public sealed record AspsDiscovery(
    bool HasMessages,
    IReadOnlyList<AspsChatMessage> Messages);

/// <summary>
/// Prototype information from ASPS.ai including build status and feedback.
/// </summary>
public sealed record AspsPrototypeInfo(
    bool Generating,
    bool Failed,
    string? ErrorMessage,
    bool HasPrototype,
    AspsPrototypeDetail? Prototype,
    IReadOnlyList<AspsPrototypeFeedback> Feedback);

/// <summary>
/// Details of a deployed ASPS.ai prototype.
/// </summary>
public sealed record AspsPrototypeDetail(
    int Id,
    string Name,
    int Version,
    string? PreviewUrl,
    DateTimeOffset CreatedAt);

/// <summary>
/// User feedback item for an ASPS.ai prototype.
/// </summary>
public sealed record AspsPrototypeFeedback(
    int Id,
    string Feedback,
    string? SectionKey,
    string? ElementSelector,
    int PrototypeId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Generation and availability status flags for an ASPS.ai project.
/// </summary>
public sealed record AspsProjectStatus(
    bool HasContent,
    bool HasLastenheft,
    bool HasPflichtenheft,
    bool LastenheftGenerating,
    bool PrototypeGenerating,
    bool PrototypeHasFiles,
    bool PrototypeFailed,
    DateTimeOffset? LastenheftAbnahmeAt,
    DateTimeOffset? PflichtenheftAbnahmeAt,
    string? LastenheftPhase,
    string? LastenheftStreamContent,
    string? PrototypePhase,
    string? PrototypeStreamContent,
    int? PrototypeId);

/// <summary>
/// Result of updating task priorities via the ASPS.ai API.
/// </summary>
public sealed record AspsTaskPriority(
    int? ImpactPriority,
    int? EffortPriority);

/// <summary>
/// Result of a report submission to ASPS.ai (prototype, Pflichtenheft, or build status).
/// </summary>
/// <param name="Message">Confirmation message from ASPS.ai.</param>
/// <param name="Id">Optional ID of the created or updated record.</param>
public sealed record AspsReportResult(string Message, int? Id);

/// <summary>
/// Result of deploying a prototype via MCP to GitLab.
/// </summary>
/// <param name="Success">Whether the deployment succeeded.</param>
/// <param name="PagesUrl">GitLab Pages URL where the prototype is accessible.</param>
/// <param name="CommitSha">Commit SHA of the deployed files.</param>
/// <param name="SubfolderPath">Path within the infrastructure/asps repo.</param>
/// <param name="Error">Error message if deployment failed.</param>
public sealed record McpDeployResult(
    bool Success,
    string? PagesUrl,
    string? CommitSha,
    string? SubfolderPath,
    string? Error);

/// <summary>
/// Result of the ASPS merge pipeline (Step 11): merge, verification, and optional MR creation.
/// </summary>
/// <param name="Merge">Git merge result with commit hash and conflict information.</param>
/// <param name="Verification">Build/test verification result. Null if no verify command configured.</param>
/// <param name="MergeRequestUrl">URL of the created merge request. Null if MR not created.</param>
/// <param name="FinalState">Final project state after merge pipeline.</param>
public sealed record AspsMergeResult(
    MergeResult Merge,
    DevVerificationResult? Verification,
    string? MergeRequestUrl,
    DevProjectState FinalState);

/// <summary>
/// Result of the ASPS delivery phase (Step 12): report, AKG updates, and archival.
/// </summary>
/// <param name="ProjectId">Internal Edda project identifier.</param>
/// <param name="ReportMarkdown">Generated Markdown report summarizing the entire project.</param>
/// <param name="MergeRequestUrl">URL of the merge request, if available.</param>
/// <param name="RulesUpdated">Number of AKG rules updated with completion status.</param>
/// <param name="CompletedAt">Timestamp when the project was archived.</param>
public sealed record AspsDeliveryReport(
    string ProjectId,
    string ReportMarkdown,
    string? MergeRequestUrl,
    int RulesUpdated,
    DateTimeOffset CompletedAt);

/// <summary>
/// Pipeline step identifier for checkpoint tracking.
/// </summary>
public enum PipelineStep
{
    /// <summary>Not started.</summary>
    NotStarted = 0,
    /// <summary>Project imported, AKG domain created.</summary>
    Imported = 1,
    /// <summary>Development plan built.</summary>
    Planned = 2,
    /// <summary>Prototype generated and deployed (or skipped).</summary>
    Prototyped = 3,
    /// <summary>Prompts decomposed into agent packages.</summary>
    Decomposed = 4,
    /// <summary>Agents executed and monitoring complete.</summary>
    AgentsCompleted = 5,
    /// <summary>Code review completed (with fix rounds if needed).</summary>
    Reviewed = 6,
    /// <summary>Branches merged and verified.</summary>
    Merged = 7,
    /// <summary>Results delivered, AKG updated, project archived.</summary>
    Delivered = 8,
}

/// <summary>
/// Checkpoint for the ASPS pipeline, enabling resume after failure.
/// Stores the last completed step and serialized intermediate results.
/// </summary>
public sealed class PipelineCheckpoint
{
    /// <summary>Unique run identifier.</summary>
    public required string RunId { get; init; }

    /// <summary>ASPS project slug.</summary>
    public required string AspsSlug { get; init; }

    /// <summary>User who started the pipeline.</summary>
    public required string UserId { get; init; }

    /// <summary>Internal project ID (set after import step).</summary>
    public string? ProjectId { get; set; }

    /// <summary>Last successfully completed step.</summary>
    public PipelineStep CompletedStep { get; set; } = PipelineStep.NotStarted;

    /// <summary>Whether the prototype has been approved by the ASPS stakeholder.</summary>
    public bool PrototypeApproved { get; set; }

    /// <summary>Number of feedback iterations completed (for observability).</summary>
    public int FeedbackIterations { get; set; }

    /// <summary>JSON-serialized intermediate state for resume.</summary>
    public string? StateJson { get; set; }

    /// <summary>When the checkpoint was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Error message if the pipeline failed at this step.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Full result of the ASPS orchestration pipeline (Steps 01-12).
/// </summary>
/// <param name="ProjectId">Internal Edda project identifier.</param>
/// <param name="AspsSlug">ASPS.ai project slug.</param>
/// <param name="Plan">The generated development plan.</param>
/// <param name="PrototypeUrl">URL of the deployed prototype. Null if prototype was skipped.</param>
/// <param name="MergeResult">Result from the merge pipeline.</param>
/// <param name="ReviewResult">Result from the code review. Null if review was skipped.</param>
/// <param name="DeliveryReport">Final delivery report with AKG updates and archival status.</param>
public sealed record AspsOrchestrationResult(
    string ProjectId,
    string AspsSlug,
    DevPlan Plan,
    string? PrototypeUrl,
    AspsMergeResult MergeResult,
    ReviewResult? ReviewResult,
    AspsDeliveryReport DeliveryReport);
