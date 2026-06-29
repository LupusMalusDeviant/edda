namespace Edda.Core.Models;

/// <summary>
/// A single result from a web search query.
/// </summary>
/// <param name="Title">The page title.</param>
/// <param name="Url">The URL of the result.</param>
/// <param name="Snippet">A short excerpt describing the page content.</param>
public sealed record SearchResult(string Title, string Url, string Snippet);
