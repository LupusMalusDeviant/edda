namespace Edda.Core.Abstractions;

/// <summary>
/// Abstracts the ability to download raw attachment bytes from a channel-specific
/// storage or CDN using an opaque reference string.
/// </summary>
/// <remarks>
/// Each channel provides its own implementation:
/// <list type="bullet">
///   <item>Telegram — resolves a <c>file_id</c> via <c>getFile</c> and downloads from the Telegram CDN.</item>
///   <item>Matrix — resolves an <c>mxc://</c> URI via the media download endpoint.</item>
///   <item>REST / Web-UI — attachments are uploaded directly; no download step required.</item>
/// </list>
/// The returned bytes are then base64-encoded and wrapped in a <see cref="Models.ContentPart"/>
/// for the agent pipeline.
/// </remarks>
public interface IAttachmentSource
{
    /// <summary>
    /// Downloads the raw bytes of an attachment identified by a channel-specific
    /// reference string (e.g. a Telegram <c>file_id</c>, a Matrix <c>mxc://</c> URI).
    /// </summary>
    /// <param name="attachmentRef">The opaque channel-specific identifier of the attachment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw file bytes.</returns>
    Task<byte[]> DownloadAttachmentAsync(string attachmentRef, CancellationToken ct);
}
