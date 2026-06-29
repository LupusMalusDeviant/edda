using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// External UI design generator (e.g. Google Stitch, v0.dev, Vercel) wrapped
/// as a provider so the <c>UiSubAgent</c> can optionally enrich its prompt
/// with a design reference. Providers must handle their own quota/rate
/// limiting — returning <c>null</c> signals "no reference available this time"
/// so the UI sub-agent falls back to its default Claude-only path.
/// </summary>
public interface IUiDesignProvider
{
    /// <summary>Stable identifier (e.g. <c>"stitch"</c>, <c>"v0"</c>).</summary>
    string ProviderName { get; }

    /// <summary>
    /// Whether the provider is currently available (configured + quota allowing).
    /// Called cheaply before <see cref="GenerateReferenceAsync"/> so the UI
    /// sub-agent can skip the call entirely when quota is exhausted.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> IsAvailableAsync(CancellationToken ct);

    /// <summary>
    /// Generates a design reference for the given project. Returns null when
    /// the provider cannot fulfil the request (quota, API error, no content)
    /// — the UI sub-agent then proceeds without external design input.
    /// Must not throw for service-side failures.
    /// </summary>
    /// <param name="request">Project metadata + optional style hint.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A design reference or null.</returns>
    Task<UiDesignReference?> GenerateReferenceAsync(
        UiDesignRequest request,
        CancellationToken ct);
}
