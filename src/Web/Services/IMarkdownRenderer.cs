using Microsoft.AspNetCore.Components;

namespace Edda.Web.Services;

/// <summary>
/// Renders Markdown text to safe HTML for display in the UI. Implementations must sanitise the output so
/// that untrusted rule bodies cannot inject executable markup (e.g. <c>&lt;script&gt;</c>).
/// </summary>
public interface IMarkdownRenderer
{
    /// <summary>Renders <paramref name="markdown"/> to sanitised HTML.</summary>
    /// <param name="markdown">The Markdown source (may be <c>null</c>, empty or whitespace).</param>
    /// <returns>Safe HTML wrapped as a <see cref="MarkupString"/>; an empty markup for null/blank input.</returns>
    MarkupString RenderToHtml(string? markdown);
}
