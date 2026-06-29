namespace Edda.Core.Models;

/// <summary>
/// Controls when <c>IBrowserProxy.NavigateAsync</c> returns relative to the page loading state.
/// </summary>
public enum WaitCondition
{
    /// <summary>Wait until the <c>load</c> event fires (all resources loaded).</summary>
    Load,

    /// <summary>Wait until <c>DOMContentLoaded</c> fires (HTML parsed, before images).</summary>
    DomContentLoaded,

    /// <summary>Wait until there are no pending network requests for at least 500 ms.</summary>
    NetworkIdle,
}

/// <summary>
/// Describes the state of the browser page after a navigation operation.
/// </summary>
/// <param name="Url">The final URL after any redirects.</param>
/// <param name="Title">The page title as reported by the browser.</param>
/// <param name="StatusCode">The HTTP status code of the main navigation response.</param>
public sealed record BrowserPageState(string Url, string Title, int StatusCode);
