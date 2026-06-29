using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Smoke-tests a deployed prototype URL before it is reported to ASPS.ai.
/// Loads the start page, crawls a limited number of internal links, checks
/// HTTP status + JS console errors, and returns a structured verdict.
/// </summary>
/// <remarks>
/// Implementations must never throw — service-side failures are captured
/// in <see cref="SmokeTestResult.Error"/> so the calling step can still
/// complete. When the browser proxy is unavailable, a null-implementation
/// returns a neutral "skipped" result.
/// </remarks>
public interface IPrototypeSmokeTest
{
    /// <summary>
    /// Runs the smoke test against <paramref name="baseUrl"/>.
    /// </summary>
    /// <param name="baseUrl">Absolute URL of the prototype start page.</param>
    /// <param name="maxPages">Maximum number of pages to crawl (including the start page).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SmokeTestResult> RunAsync(
        string baseUrl,
        int maxPages,
        CancellationToken ct);
}
