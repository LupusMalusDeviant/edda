namespace Edda.Core.Models;

/// <summary>
/// Aggregated result of a prototype smoke-test run over multiple pages.
/// Surfaces to ASPS.ai alongside the prototype URL so the reviewer sees
/// immediately whether the generated click-dummy actually loads.
/// </summary>
/// <param name="Success">True when every checked page returned 2xx and raised no console errors.</param>
/// <param name="Pages">Per-page breakdown — empty when the test was skipped.</param>
/// <param name="Summary">Human-readable one-liner for logs and ASPS reports.</param>
/// <param name="Error">Fatal error message if the test could not execute at all.</param>
public sealed record SmokeTestResult(
    bool Success,
    IReadOnlyList<SmokePageResult> Pages,
    string Summary,
    string? Error);

/// <summary>
/// Smoke-test outcome for a single page.
/// </summary>
/// <param name="Url">Absolute URL that was loaded.</param>
/// <param name="StatusCode">HTTP status code of the main navigation response.</param>
/// <param name="Title">Page title as reported by the browser, or null if the page did not load.</param>
/// <param name="ConsoleErrors">Errors captured via <c>console.error</c> / <c>window.onerror</c>.</param>
/// <param name="InternalLinks">Internal links discovered on the page (used by the crawler).</param>
public sealed record SmokePageResult(
    string Url,
    int StatusCode,
    string? Title,
    IReadOnlyList<string> ConsoleErrors,
    IReadOnlyList<string> InternalLinks);
