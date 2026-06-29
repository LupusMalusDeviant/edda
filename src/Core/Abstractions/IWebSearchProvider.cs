using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Abstracts web search providers for the web_search tool.
/// Add a new provider by implementing this interface and configuring SEARCH_PROVIDER.
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>
    /// Unique provider identifier.
    /// Known values: "brave", "serper", "duckduckgo".
    /// </summary>
    string ProviderName { get; }

    /// <summary>True if this provider requires an API key to function.</summary>
    bool RequiresApiKey { get; }

    /// <summary>
    /// Executes a web search query and returns ranked results.
    /// </summary>
    /// <param name="query">The search query string.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Search results ordered by relevance.</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default);
}
