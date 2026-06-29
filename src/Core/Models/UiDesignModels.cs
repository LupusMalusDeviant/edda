namespace Edda.Core.Models;

/// <summary>
/// Request to a <see cref="Abstractions.IUiDesignProvider"/> for a design
/// reference used by the UI sub-agent. The provider (e.g. Google Stitch) may
/// return an HTML/CSS sample, a token set, or a screenshot URL — everything
/// optional, the caller uses whatever the provider can supply.
/// </summary>
/// <param name="ProjectName">Human-readable project name (used as title).</param>
/// <param name="Description">Summary of the Lastenheft — 1–3 paragraphs.</param>
/// <param name="Feedback">Optional iteration feedback from ASPS.ai.</param>
/// <param name="Style">Optional style hint (e.g. "minimal", "modern dark", "enterprise").</param>
public sealed record UiDesignRequest(
    string ProjectName,
    string Description,
    string? Feedback,
    string? Style);

/// <summary>
/// Design reference returned by a <see cref="Abstractions.IUiDesignProvider"/>.
/// Consumed by the UI sub-agent as additional context when generating the
/// full multi-page prototype. All fields are optional — the provider returns
/// whatever it can deliver and the UI sub-agent degrades gracefully on nulls.
/// </summary>
/// <param name="ProviderName">Identifier of the source ("stitch", "v0", …).</param>
/// <param name="SampleHtml">Optional sample HTML snippet embodying the design language.</param>
/// <param name="TailwindConfigJson">Optional extracted design tokens (colors, typography, spacing) as JSON.</param>
/// <param name="ScreenshotUrl">Optional absolute URL to a rendered preview image.</param>
/// <param name="Notes">Optional human-readable notes from the provider.</param>
public sealed record UiDesignReference(
    string ProviderName,
    string? SampleHtml,
    string? TailwindConfigJson,
    string? ScreenshotUrl,
    string? Notes);
