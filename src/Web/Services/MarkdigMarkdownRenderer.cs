using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace Edda.Web.Services;

/// <summary>
/// <see cref="IMarkdownRenderer"/> backed by Markdig (self-hosted NuGet, no CDN — see CLAUDE.md rule 12).
/// Raw inline and block HTML is disabled, so embedded markup such as <c>&lt;script&gt;</c> is emitted as
/// escaped text and never executed. Only a small, safe set of extensions is enabled (pipe tables and
/// auto-links) — the full "advanced" bundle is deliberately avoided because it includes generic attributes,
/// which would let a body inject arbitrary HTML attributes (e.g. event handlers). As a further safeguard,
/// link and image URLs using dangerous schemes (<c>javascript:</c>, <c>vbscript:</c>, <c>data:</c>) are
/// neutralised.
/// </summary>
public sealed class MarkdigMarkdownRenderer : IMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseAutoLinks()
        .Build();

    private static readonly string[] DangerousSchemes = ["javascript:", "vbscript:", "data:"];

    /// <inheritdoc />
    public MarkupString RenderToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new MarkupString(string.Empty);

        var document = Markdown.Parse(markdown, Pipeline);

        // Markdig does not validate URL schemes; strip dangerous ones from links and images.
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (IsDangerousUrl(link.Url))
                link.Url = string.Empty;
        }

        var builder = new StringBuilder();
        using var writer = new StringWriter(builder);
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return new MarkupString(builder.ToString());
    }

    private static bool IsDangerousUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var normalized = url.Trim().ToLowerInvariant();
        foreach (var scheme in DangerousSchemes)
        {
            if (normalized.StartsWith(scheme, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
