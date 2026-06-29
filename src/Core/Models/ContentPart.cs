namespace Edda.Core.Models;

/// <summary>
/// Represents a single part of a multimodal message.
/// A message may contain any combination of text and image parts.
/// </summary>
public sealed record ContentPart
{
    /// <summary>Discriminates whether this part carries text or image data.</summary>
    public required ContentPartType Type { get; init; }

    /// <summary>
    /// Plain text content. Only set when <see cref="Type"/> is <see cref="ContentPartType.Text"/>.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Base64-encoded raw image bytes.
    /// Only set when <see cref="Type"/> is <see cref="ContentPartType.Image"/>.
    /// </summary>
    public string? ImageData { get; init; }

    /// <summary>
    /// MIME type of the image (e.g. <c>"image/jpeg"</c>, <c>"image/png"</c>,
    /// <c>"image/gif"</c>, <c>"image/webp"</c>).
    /// Only set when <see cref="Type"/> is <see cref="ContentPartType.Image"/>.
    /// </summary>
    public string? MediaType { get; init; }

    /// <summary>Creates a text content part.</summary>
    /// <param name="text">The text content.</param>
    /// <returns>A <see cref="ContentPart"/> of type <see cref="ContentPartType.Text"/>.</returns>
    public static ContentPart FromText(string text) =>
        new() { Type = ContentPartType.Text, Text = text };

    /// <summary>
    /// Creates an image content part from base64-encoded data.
    /// </summary>
    /// <param name="base64Data">Base64-encoded image bytes.</param>
    /// <param name="mediaType">MIME type (e.g. <c>"image/jpeg"</c>).</param>
    /// <returns>A <see cref="ContentPart"/> of type <see cref="ContentPartType.Image"/>.</returns>
    public static ContentPart FromBase64Image(string base64Data, string mediaType) =>
        new() { Type = ContentPartType.Image, ImageData = base64Data, MediaType = mediaType };
}

/// <summary>Discriminates the kind of content carried by a <see cref="ContentPart"/>.</summary>
public enum ContentPartType
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>Base64-encoded image.</summary>
    Image
}
