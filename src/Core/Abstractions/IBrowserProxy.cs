using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Abstracts the browser automation proxy service running Playwright in an isolated container.
/// All browser operations are executed remotely via HTTP — the agent process never runs a browser directly.
/// </summary>
public interface IBrowserProxy
{
    /// <summary>
    /// Creates a new isolated browser session (one BrowserContext per session).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A unique session ID to pass to subsequent browser operations.</returns>
    Task<string> CreateSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Navigates the session's page to the given URL and waits until the specified condition.
    /// </summary>
    /// <param name="sessionId">The active browser session.</param>
    /// <param name="url">Absolute URL to navigate to.</param>
    /// <param name="waitUntil">How long to wait before returning (default: DomContentLoaded).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resulting page state (URL, title, HTTP status code).</returns>
    Task<BrowserPageState> NavigateAsync(
        string sessionId,
        string url,
        WaitCondition waitUntil = WaitCondition.DomContentLoaded,
        CancellationToken ct = default);

    /// <summary>
    /// Clicks the first element matching the given CSS selector on the current page.
    /// </summary>
    /// <param name="sessionId">The active browser session.</param>
    /// <param name="selector">CSS selector identifying the target element.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ClickAsync(string sessionId, string selector, CancellationToken ct = default);

    /// <summary>
    /// Types text into the first form element matching the given CSS selector.
    /// </summary>
    /// <param name="sessionId">The active browser session.</param>
    /// <param name="selector">CSS selector for the input element.</param>
    /// <param name="text">Text to type into the element.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FillAsync(string sessionId, string selector, string text, CancellationToken ct = default);

    /// <summary>
    /// Takes a full-page screenshot of the current browser page.
    /// </summary>
    /// <param name="sessionId">The active browser session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>PNG image data as a byte array.</returns>
    Task<byte[]> ScreenshotAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Extracts the current page's visible text content.
    /// </summary>
    /// <param name="sessionId">The active browser session.</param>
    /// <param name="asMarkdown">When true, converts HTML to Markdown; when false, returns plain text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page's text content in the requested format.</returns>
    Task<string> ReadPageAsync(
        string sessionId,
        bool asMarkdown = true,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a JavaScript expression or function body in the current page context.
    /// </summary>
    /// <param name="sessionId">The active browser session.</param>
    /// <param name="script">JavaScript code to evaluate. Return value is serialized to JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The JSON-serialized result of the script evaluation.</returns>
    Task<string> EvaluateAsync(string sessionId, string script, CancellationToken ct = default);

    /// <summary>
    /// Destroys a browser session and releases all associated resources.
    /// </summary>
    /// <param name="sessionId">The session to close.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CloseSessionAsync(string sessionId, CancellationToken ct = default);
}
