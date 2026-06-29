namespace Edda.Core.Abstractions;

/// <summary>
/// Abstracts output channels for trigger notifications and task queue results.
/// Add a new delivery channel by implementing this interface and registering it in DI.
/// </summary>
public interface IDeliveryChannel
{
    /// <summary>
    /// Unique channel identifier used in trigger/task configuration.
    /// Known values: "telegram", "log", "dashboard".
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// Delivers content through this channel to the specified user.
    /// </summary>
    /// <param name="content">The message or result content to deliver.</param>
    /// <param name="userId">Target user ID for routing. Null for broadcast/log channels.</param>
    /// <param name="metadata">Optional channel-specific routing metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeliverAsync(
        string content,
        string? userId,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delivers a file (binary content) through this channel to the specified user.
    /// Default implementation falls back to <see cref="DeliverAsync"/> with a text representation.
    /// Override in channels that support native file delivery (e.g. Telegram sendDocument).
    /// </summary>
    /// <param name="fileContent">Raw bytes of the file to deliver.</param>
    /// <param name="fileName">Suggested file name for the recipient.</param>
    /// <param name="mimeType">MIME type of the file (e.g. "text/plain", "image/png"). Null for auto-detect.</param>
    /// <param name="caption">Optional caption text sent alongside the file.</param>
    /// <param name="userId">Target user ID for routing.</param>
    /// <param name="metadata">Optional channel-specific routing metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the file was delivered natively; false if it fell back to text delivery.</returns>
    Task<bool> DeliverFileAsync(
        byte[] fileContent,
        string fileName,
        string? mimeType,
        string? caption,
        string? userId,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        // Default: fall back to text delivery with file content as text
        var text = $"📎 {fileName}";
        if (caption is not null)
            text = $"{caption}\n\n{text}";
        return DeliverAsync(text, userId, metadata, ct).ContinueWith(_ => false, ct);
    }
}
