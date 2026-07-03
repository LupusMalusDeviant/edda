using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Manages the agent's long-term memory for a user.
/// Reads, writes, and clears <c>data/users/{userId}/memory.md</c>.
/// Supports actions: <c>read</c>, <c>write</c>, <c>clear</c>.
/// </summary>
internal sealed class ManageMemoryTool : IAgentTool
{
    private const long MaxFileSizeBytes = 100 * 1024; // 100 KB
    private const string MemoryFileName = "memory.md";

    private readonly IFileSystem _fs;
    private readonly ILogger<ManageMemoryTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "manage_memory",
        Description = "Reads, writes, or clears the agent's long-term memory for the current user. " +
                      "Use this to persist facts, preferences, or context across sessions.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                action = new { type = "string", description = "Action to perform: read, write, or clear." },
                content = new { type = "string", description = "Content to write. Required for action=write." }
            },
            required = new[] { "action" }
        }
    };

    /// <summary>
    /// Initializes a new <see cref="ManageMemoryTool"/>.
    /// </summary>
    /// <param name="fileSystem">Abstracted file system for all I/O.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="authorizer">C2: central role gate — writing requires Editor. Null permits (legacy).</param>
    public ManageMemoryTool(IFileSystem fileSystem, ILogger<ManageMemoryTool> logger,
        IRuleAuthorizer? authorizer = null)
    {
        _fs = fileSystem;
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
            var path = _fs.CombinePath("data", "users", userId, MemoryFileName);

            // C2: only the mutating actions are role-gated — Viewers may still read (matrix row 1).
            if (action is "write" or "clear" && !MemoryToolAuthorization.MayMutate(_authorizer))
                return ToolResult.Fail(call.Id, Definition.Name, MemoryToolAuthorization.InsufficientRoleMessage);

            _logger.LogInformation("manage_memory action={Action} userId={UserId}", action, userId);

            return action switch
            {
                "read" => await ReadAsync(call, path, cancellationToken),
                "write" => await WriteAsync(call, path, cancellationToken),
                "clear" => ClearFile(call, path),
                _ => ToolResult.Fail(call.Id, Definition.Name, $"Unknown action '{action}'. Supported: read, write, clear.")
            };
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "manage_memory unexpected error");
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

    private async Task<ToolResult> WriteAsync(ToolCall call, string path, CancellationToken ct)
    {
        var content = ToolArgumentHelper.GetRequiredString(call.Arguments, "content");
        if (System.Text.Encoding.UTF8.GetByteCount(content) > MaxFileSizeBytes)
            return ToolResult.Fail(call.Id, Definition.Name,
                $"Content exceeds the 100 KB limit for memory files.");
        _fs.EnsureDirectoryExists(System.IO.Path.GetDirectoryName(path)!);
        await _fs.WriteAllTextAsync(path, content, ct);
        return ToolResult.Ok(call.Id, Definition.Name, "Memory written successfully.");
    }

    private ToolResult ClearFile(ToolCall call, string path)
    {
        _fs.DeleteFile(path);
        return ToolResult.Ok(call.Id, Definition.Name, "Memory cleared.");
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
