using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Manages learned corrections and user preferences for the current user.
/// Reads, appends to, and clears <c>data/users/{userId}/learnings.md</c>.
/// Supports actions: <c>read</c>, <c>append</c>, <c>clear</c>.
/// Each appended entry is timestamped: <c>[YYYY-MM-DD HH:mm] content</c>.
/// After each append or clear, the learning state is mirrored as a <c>:Rule</c> node
/// in the AKG domain <c>learnings</c> so it appears in the knowledge graph view.
/// </summary>
internal sealed class ManageLearningsTool : IAgentTool
{
    private const long MaxFileSizeBytes = 100 * 1024; // 100 KB
    private const string LearningsFileName = "learnings.md";
    private const string LearningsDomain = "learnings";

    private readonly IFileSystem _fs;
    private readonly TimeProvider _timeProvider;
    private readonly IKnowledgeGraph _knowledgeGraph;
    private readonly ILogger<ManageLearningsTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "manage_learnings",
        Description = "Reads, appends to, or clears the user's learned corrections and preferences. " +
                      "Use append to record new corrections. Each entry is timestamped automatically.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                action = new { type = "string", description = "Action to perform: read, append, or clear." },
                content = new { type = "string", description = "Correction or learning to append. Required for action=append." }
            },
            required = new[] { "action" }
        }
    };

    /// <summary>
    /// Initializes a new <see cref="ManageLearningsTool"/>.
    /// </summary>
    /// <param name="fileSystem">Abstracted file system for all I/O.</param>
    /// <param name="timeProvider">Provides the current UTC time for entry timestamps.</param>
    /// <param name="knowledgeGraph">Knowledge graph for mirroring learnings as AKG nodes.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="authorizer">C2: central role gate — writing requires Editor. Null permits (legacy).</param>
    public ManageLearningsTool(
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        IKnowledgeGraph knowledgeGraph,
        ILogger<ManageLearningsTool> logger,
        IRuleAuthorizer? authorizer = null)
    {
        _fs = fileSystem;
        _timeProvider = timeProvider;
        _knowledgeGraph = knowledgeGraph;
        _logger = logger;
        _authorizer = authorizer;
    }

    private readonly IRuleAuthorizer? _authorizer;

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = context.UserId ?? "anonymous";
            ValidateUserId(userId);

            var action = ToolArgumentHelper.GetRequiredString(call.Arguments, "action").ToLowerInvariant();
            var path = _fs.CombinePath("data", "users", userId, LearningsFileName);

            // C2: only the mutating actions are role-gated — Viewers may still read (matrix row 1).
            if (action is "append" or "clear" && !MemoryToolAuthorization.MayMutate(_authorizer))
                return ToolResult.Fail(call.Id, Definition.Name, MemoryToolAuthorization.InsufficientRoleMessage);

            _logger.LogInformation("manage_learnings action={Action} userId={UserId}", action, userId);

            return action switch
            {
                "read" => await ReadAsync(call, path, cancellationToken),
                "append" => await AppendAsync(call, path, userId, cancellationToken),
                "clear" => await ClearAsync(call, path, userId, cancellationToken),
                _ => ToolResult.Fail(call.Id, Definition.Name, $"Unknown action '{action}'. Supported: read, append, clear.")
            };
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "manage_learnings unexpected error");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }

    private async Task<ToolResult> ReadAsync(ToolCall call, string path, CancellationToken ct)
    {
        if (!_fs.FileExists(path))
            return ToolResult.Ok(call.Id, Definition.Name, string.Empty);
        var content = await _fs.ReadAllTextAsync(path, ct);
        return ToolResult.Ok(call.Id, Definition.Name, content);
    }

    private async Task<ToolResult> AppendAsync(ToolCall call, string path, string userId, CancellationToken ct)
    {
        var content = ToolArgumentHelper.GetRequiredString(call.Arguments, "content");

        // Check if appending would exceed the size limit
        string existing = string.Empty;
        if (_fs.FileExists(path))
            existing = await _fs.ReadAllTextAsync(path, ct);

        var existingSize = System.Text.Encoding.UTF8.GetByteCount(existing);
        var newEntrySize = System.Text.Encoding.UTF8.GetByteCount(content) + 30; // +30 for timestamp
        if (existingSize + newEntrySize > MaxFileSizeBytes)
            return ToolResult.Fail(call.Id, Definition.Name,
                "Appending would exceed the 100 KB limit for learnings files.");

        var timestamp = _timeProvider.GetUtcNow().ToString("yyyy-MM-dd HH:mm");
        var entry = $"[{timestamp}] {content}\n";
        _fs.EnsureDirectoryExists(System.IO.Path.GetDirectoryName(path)!);
        await _fs.AppendAllTextAsync(path, entry, ct);

        // Mirror to AKG: one learning rule per user, body = full current content
        var allLearnings = existing + entry;
        await MirrorToAkgAsync(userId, allLearnings, ct).ConfigureAwait(false);

        return ToolResult.Ok(call.Id, Definition.Name, "Learning appended successfully.");
    }

    private async Task<ToolResult> ClearAsync(ToolCall call, string path, string userId, CancellationToken ct)
    {
        _fs.DeleteFile(path);
        await RemoveFromAkgAsync(userId, ct).ConfigureAwait(false);
        return ToolResult.Ok(call.Id, Definition.Name, "Learnings cleared.");
    }

    /// <summary>
    /// Upserts a single <c>:Rule</c> node in domain <c>learnings</c> whose body mirrors
    /// all current learning entries for the given user. One node per user, ID = <c>learning-{userId}</c>.
    /// Failures are logged as warnings and never propagate to the caller.
    /// </summary>
    private async Task MirrorToAkgAsync(string userId, string allLearnings, CancellationToken ct)
    {
        try
        {
            var rule = new KnowledgeRule
            {
                Id = $"learning-{userId}",
                Type = "Learning",
                Domain = LearningsDomain,
                Priority = RulePriority.Low,
                Confidence = 1.0,
                Tags = ["learning", userId],
                Author = userId,
                Created = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime),
                Body = $"# Learnings — {userId}\n\n{allLearnings}",
                OwnerId = userId,
                SourceType = "manage_learnings",
            };
            await _knowledgeGraph.UpsertRuleAsync(rule, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mirror learning to AKG for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Deletes the AKG learning node for the given user when learnings are cleared.
    /// Failures are logged as warnings and never propagate to the caller.
    /// </summary>
    private async Task RemoveFromAkgAsync(string userId, CancellationToken ct)
    {
        try
        {
            await _knowledgeGraph.DeleteRuleAsync($"learning-{userId}", userId, isAdmin: true, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove learning from AKG for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Validates that the user ID does not contain path-traversal characters.
    /// </summary>
    /// <param name="userId">The user ID to validate.</param>
    /// <exception cref="ArgumentException">Thrown when the user ID is unsafe.</exception>
    private static void ValidateUserId(string userId)
    {
        if (userId.Contains('/') || userId.Contains('\\') || userId.Contains(".."))
            throw new ArgumentException($"Invalid userId '{userId}': must not contain path separators or '..'.");
    }
}
