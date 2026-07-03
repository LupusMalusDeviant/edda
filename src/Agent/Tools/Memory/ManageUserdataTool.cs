using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Manages structured user information for the current user.
/// Reads and writes <c>data/users/{userId}/userdata.md</c>.
/// Supports actions: <c>read</c>, <c>write</c>.
/// </summary>
internal sealed class ManageUserdataTool : IAgentTool
{
    private const long MaxFileSizeBytes = 100 * 1024; // 100 KB
    private const string UserdataFileName = "userdata.md";

    private readonly IFileSystem _fs;
    private readonly ILogger<ManageUserdataTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "manage_userdata",
        Description = "Stores or retrieves user information (name, language, preferences) so you can remember the user next time. " +
                      "When the user tells you their name or preferences and you want to remember them: use action=write or action=set with 'content' (e.g. 'Name: Joel\nLanguage: de'). " +
                      "Use action=read or get only to load already-saved data. Use delete to clear. Always pass 'action'.",
        InputSchema = new
            {
                type = "object",
                properties = new
                {
                    action = new { type = "string", description = "Required. read/get = load saved userdata. write/set = save userdata (requires 'content'). delete = clear. To remember the user, use write or set." },
                    content = new { type = "string", description = "Required for write/set. Markdown with e.g. Name:, Language:, preferences. This is what gets saved." }
                },
                required = new[] { "action" }
            }
    };

    /// <summary>
    /// Initializes a new <see cref="ManageUserdataTool"/>.
    /// </summary>
    /// <param name="fileSystem">Abstracted file system for all I/O.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="authorizer">C2: central role gate — writing requires Editor. Null permits (legacy).</param>
    public ManageUserdataTool(IFileSystem fileSystem, ILogger<ManageUserdataTool> logger,
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

            var rawAction = (ToolArgumentHelper.GetString(call.Arguments, "action") ?? "read").Trim();
            if (string.IsNullOrEmpty(rawAction)) rawAction = "read";
            rawAction = rawAction.ToLowerInvariant();
            var action = rawAction switch { "get" => "read", "set" => "write", _ => rawAction };
            var path = _fs.CombinePath("data", "users", userId, UserdataFileName);

            // C2: only the mutating actions are role-gated — Viewers may still read (matrix row 1).
            if (action is "write" or "delete" && !MemoryToolAuthorization.MayMutate(_authorizer))
                return ToolResult.Fail(call.Id, Definition.Name, MemoryToolAuthorization.InsufficientRoleMessage);

            _logger.LogInformation("manage_userdata action={Action} userId={UserId}", action, userId);

            return action switch
            {
                "read" => await ReadAsync(call, path, cancellationToken),
                "write" => await WriteAsync(call, path, cancellationToken),
                "delete" => await DeleteAsync(call, path, cancellationToken),
                _ => ToolResult.Fail(call.Id, Definition.Name, $"Unknown action '{rawAction}'. Supported: read, get, write, set, delete.")
            };
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "manage_userdata unexpected error");
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
                "Content exceeds the 100 KB limit for userdata files.");
        _fs.EnsureDirectoryExists(System.IO.Path.GetDirectoryName(path)!);
        await _fs.WriteAllTextAsync(path, content, ct);
        return ToolResult.Ok(call.Id, Definition.Name, "Userdata written successfully.");
    }

    private Task<ToolResult> DeleteAsync(ToolCall call, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_fs.FileExists(path))
            return Task.FromResult(ToolResult.Ok(call.Id, Definition.Name, "No userdata file found; nothing to delete."));
        _fs.DeleteFile(path);
        return Task.FromResult(ToolResult.Ok(call.Id, Definition.Name, "Userdata deleted."));
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
