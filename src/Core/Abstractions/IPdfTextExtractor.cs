namespace Edda.Core.Abstractions;

/// <summary>
/// Extracts plain text from an in-memory PDF document, so its content can be imported as knowledge.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extracts the concatenated text of all pages from the given PDF bytes.
    /// </summary>
    /// <param name="pdf">The raw PDF bytes.</param>
    /// <returns>The extracted text (may be empty for image-only PDFs).</returns>
    string Extract(byte[] pdf);
}
