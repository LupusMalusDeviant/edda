using System.Text;
using Edda.Core.Abstractions;
using UglyToad.PdfPig;

namespace Edda.AKG.Import;

/// <summary>
/// <see cref="IPdfTextExtractor"/> backed by PdfPig. Infrastructure adapter around a third-party library —
/// deliberately not unit-tested (consistent with the LibGit2Sharp adapter, ADR-0002); the importer's PDF
/// path is unit-tested against a fake extractor. Operates entirely in memory (no disk I/O).
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    /// <inheritdoc />
    public string Extract(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString().Trim();
    }
}
