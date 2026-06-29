namespace Edda.Core.Models;

/// <summary>
/// Configuration for a dev orchestrator project.
/// Controls repository setup, coding CLI selection, concurrency, and output options.
/// </summary>
public sealed record DevProjectConfig
{
    /// <summary>
    /// Git repository URL (HTTPS or SSH). If null, a new local repo is created.
    /// </summary>
    public string? RepoUrl { get; init; }

    /// <summary>
    /// Base branch to fork from. Default: "main".
    /// </summary>
    public string BaseBranch { get; init; } = "main";

    /// <summary>
    /// Target branch for the merged result.
    /// If null, auto-generated as "dev/orchestrator/{projectId}".
    /// </summary>
    public string? TargetBranch { get; init; }

    /// <summary>
    /// Coding CLI to use. Default: "claude-code".
    /// Supported: "claude-code", "aider", "internal" (Edda-Clone).
    /// Must match a key from <see cref="CodingCliIds"/>.
    /// </summary>
    public string CodingCli { get; init; } = CodingCliIds.ClaudeCode;

    /// <summary>
    /// Docker image for the coding agent containers.
    /// If null, resolved from <see cref="CodingCli"/>.
    /// </summary>
    public string? DockerImage { get; init; }

    /// <summary>
    /// Maximum number of coding agents running in parallel. Default: 3.
    /// </summary>
    public int MaxParallelAgents { get; init; } = 3;

    /// <summary>
    /// Maximum wall-clock time a single coding agent may run. Default: 30 minutes.
    /// </summary>
    public TimeSpan AgentTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Optional shell command to run after merge to verify the result (e.g. "dotnet build", "npm test").
    /// </summary>
    public string? VerifyCommand { get; init; }

    /// <summary>
    /// Whether to automatically push the merged result. Default: false.
    /// </summary>
    public bool AutoPush { get; init; }

    /// <summary>
    /// Whether to create a pull request after push. Requires <see cref="AutoPush"/> = true.
    /// </summary>
    public bool CreatePullRequest { get; init; }

    /// <summary>
    /// Credential key in ICredentialStore for git auth. Required for private repos and push/PR.
    /// </summary>
    public string? GitCredentialKey { get; init; }

    /// <summary>
    /// Delivery channel for progress reports. Default: "log".
    /// </summary>
    public string ReportChannel { get; init; } = "log";

    /// <summary>
    /// Additional environment variables passed to all coding agent containers.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtraEnvVars { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Optional prototype feedback text sent by ASPS.ai in the config field.
    /// When set, the prototype step uses RebuildWithFeedbackAsync on IPrototypeBuilder
    /// instead of BuildPrototypeAsync to incorporate user feedback.
    /// This is a fallback mechanism for when ASPS.ai sends feedback via config rather than
    /// the dedicated feedback endpoint.
    /// </summary>
    public string? PrototypeFeedback { get; init; }
}

/// <summary>
/// A decomposed development plan produced by project analysis.
/// Contains the task dependency graph, agent role assignments, and AKG domain scoping.
/// </summary>
public sealed record DevPlan
{
    /// <summary>Unique identifier for this plan.</summary>
    public required string PlanId { get; init; }

    /// <summary>Short human-readable project name.</summary>
    public required string ProjectName { get; init; }

    /// <summary>Summary of the original specification.</summary>
    public required string SpecSummary { get; init; }

    /// <summary>The project configuration.</summary>
    public required DevProjectConfig Config { get; init; }

    /// <summary>The decomposed tasks with dependency information.</summary>
    public required IReadOnlyList<DevTask> Tasks { get; init; }

    /// <summary>
    /// The generated <see cref="WorkflowDefinition"/> (DAG) derived from the tasks.
    /// Can be reviewed and modified before execution.
    /// </summary>
    public required WorkflowDefinition Workflow { get; init; }
}

/// <summary>
/// A single development task assigned to a coding agent.
/// Contains the instruction, role, AKG scoping, git branch, and file scope.
/// </summary>
public sealed record DevTask
{
    /// <summary>Unique task identifier within this plan (kebab-case).</summary>
    public required string TaskId { get; init; }

    /// <summary>Human-readable task description.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// Detailed instruction for the coding agent. Contains full context:
    /// what to implement, constraints, file paths, coding conventions.
    /// </summary>
    public required string Instruction { get; init; }

    /// <summary>
    /// Agent role: "frontend", "backend", "database", "devops", "fullstack", "testing", etc.
    /// Maps to a Skill Profile name (F29).
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// AKG domain names relevant to this task. The coding agent receives only
    /// rules from these domains. Example: ["frontend", "security", "technisch.react"].
    /// </summary>
    public required IReadOnlyList<string> AkgDomains { get; init; }

    /// <summary>
    /// Git branch name for this task. Example: "dev/frontend-auth-ui".
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Task IDs this task depends on. Empty means the task can run immediately.
    /// </summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>
    /// Directories/files this agent should focus on. Used for context scoping.
    /// Example: ["src/frontend/", "src/shared/types/"].
    /// </summary>
    public IReadOnlyList<string> FileScope { get; init; } = [];

    /// <summary>
    /// Expected output files/artifacts this task should produce.
    /// Used for verification after completion.
    /// </summary>
    public IReadOnlyList<string> ExpectedOutputs { get; init; } = [];

    /// <summary>
    /// Estimated complexity: "low", "medium", "high".
    /// Influences timeout and resource allocation.
    /// </summary>
    public string Complexity { get; init; } = "medium";
}

/// <summary>
/// Lightweight handle to a running or completed dev project.
/// </summary>
/// <param name="ProjectId">Unique project identifier.</param>
/// <param name="ProjectName">Human-readable project name.</param>
/// <param name="UserId">Owner of the project.</param>
/// <param name="State">Current project state.</param>
/// <param name="CreatedAt">When the project was created.</param>
/// <param name="CompletedAt">When the project finished, or null if still running.</param>
public sealed record DevProjectHandle(
    string ProjectId,
    string ProjectName,
    string UserId,
    DevProjectState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// Detailed status of a dev project including per-agent state.
/// </summary>
public sealed record DevProjectStatus
{
    /// <summary>Unique project identifier.</summary>
    public required string ProjectId { get; init; }

    /// <summary>Current project state.</summary>
    public required DevProjectState State { get; init; }

    /// <summary>Per-agent status keyed by task ID.</summary>
    public required IReadOnlyDictionary<string, DevAgentStatus> AgentStates { get; init; }

    /// <summary>Merge commit hash after successful merge. Null if not yet merged.</summary>
    public string? MergeCommitHash { get; init; }

    /// <summary>Pull request URL if a PR was created. Null otherwise.</summary>
    public string? PullRequestUrl { get; init; }

    /// <summary>Error message if the project failed. Null on success.</summary>
    public string? Error { get; init; }

    /// <summary>When the project finished. Null if still running.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Verification result if a verify command was configured.</summary>
    public DevVerificationResult? Verification { get; init; }
}

/// <summary>
/// Status of a single coding agent within a dev project.
/// </summary>
/// <param name="TaskId">The task this agent is working on.</param>
/// <param name="Role">Agent role (frontend, backend, etc.).</param>
/// <param name="BranchName">Git branch this agent is committing to.</param>
/// <param name="State">Current agent state.</param>
/// <param name="CommitCount">Number of commits made so far. Null if not yet started.</param>
/// <param name="LastCommitMessage">Most recent commit message. Null if no commits yet.</param>
/// <param name="Error">Error detail if the agent failed. Null on success.</param>
public sealed record DevAgentStatus(
    string TaskId,
    string Role,
    string BranchName,
    DevAgentState State,
    int? CommitCount,
    string? LastCommitMessage,
    string? Error);

/// <summary>
/// Result of running the verification command after merge.
/// </summary>
/// <param name="Success">True if the command exited with code 0.</param>
/// <param name="Command">The command that was executed.</param>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="Stdout">Standard output captured from the process.</param>
/// <param name="Stderr">Standard error captured from the process.</param>
public sealed record DevVerificationResult(
    bool Success,
    string Command,
    int ExitCode,
    string Stdout,
    string Stderr);

/// <summary>
/// Result of merging all agent branches into the target branch.
/// </summary>
/// <param name="Success">True if all merges succeeded without unresolved conflicts.</param>
/// <param name="MergeCommitHash">The final merge commit hash. Null on failure.</param>
/// <param name="ConflictFiles">List of files with merge conflicts. Empty on success.</param>
/// <param name="Error">Error description. Null on success.</param>
public sealed record MergeResult(
    bool Success,
    string? MergeCommitHash,
    IReadOnlyList<string> ConflictFiles,
    string? Error)
{
    /// <summary>Agent branches that did not exist during merge. Non-empty means some agents failed to run.</summary>
    public IReadOnlyList<string> MissingBranches { get; init; } = [];
}

/// <summary>
/// Information about the prepared git repository for a dev project.
/// </summary>
/// <param name="LocalPath">Absolute path to the local repository.</param>
/// <param name="BaseBranch">The base branch that was forked from.</param>
/// <param name="TargetBranch">The target branch for merged results.</param>
/// <param name="IsNewRepo">True if the repo was newly initialized (not cloned).</param>
public sealed record GitRepoInfo(
    string LocalPath,
    string BaseBranch,
    string TargetBranch,
    bool IsNewRepo);

/// <summary>
/// A timestamped log entry in the dev project execution history.
/// </summary>
/// <param name="Timestamp">When this event occurred.</param>
/// <param name="Phase">Execution phase (planning, spawning, merging, verifying, etc.).</param>
/// <param name="Message">Human-readable description of the event.</param>
/// <param name="TaskId">Optional task ID if the event relates to a specific task.</param>
/// <param name="Level">Log severity level as string ("Information", "Warning", "Error", etc.).</param>
public sealed record DevProjectLogEntry(
    DateTimeOffset Timestamp,
    string Phase,
    string Message,
    string? TaskId,
    string Level);

/// <summary>
/// Handle to a spawned coding agent container.
/// </summary>
/// <param name="AgentId">Unique agent identifier (e.g. "dev-frontend-abc123").</param>
/// <param name="ContainerId">Docker container ID.</param>
/// <param name="TaskId">The dev task this agent is executing.</param>
public sealed record CodingAgentHandle(
    string AgentId,
    string ContainerId,
    string TaskId);

/// <summary>Lifecycle states of a dev orchestrator project.</summary>
public enum DevProjectState
{
    /// <summary>Plan is being created by the LLM.</summary>
    Planning,

    /// <summary>Plan is ready, awaiting user confirmation.</summary>
    Ready,

    /// <summary>Coding agents are actively working.</summary>
    Running,

    /// <summary>Agent branches are being merged.</summary>
    Merging,

    /// <summary>Verification command is running.</summary>
    Verifying,

    /// <summary>All work completed successfully.</summary>
    Completed,

    /// <summary>An error occurred during execution.</summary>
    Failed,

    /// <summary>Cancelled by the user.</summary>
    Cancelled
}

/// <summary>Lifecycle states of a single coding agent within a dev project.</summary>
public enum DevAgentState
{
    /// <summary>Waiting for dependency tasks to complete.</summary>
    Pending,

    /// <summary>Docker container is being started.</summary>
    Spawning,

    /// <summary>Agent is actively working on its task.</summary>
    Running,

    /// <summary>Agent finished and pushed its commits.</summary>
    Completed,

    /// <summary>Agent encountered an error.</summary>
    Failed
}
