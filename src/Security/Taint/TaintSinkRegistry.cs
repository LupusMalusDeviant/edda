using Edda.Core.Models;

namespace Edda.Security.Taint;

/// <summary>
/// Defines which taint labels are forbidden from flowing into which tools (sink definitions).
/// Loaded from default configuration and optionally extended via the
/// <c>TAINT_EXTRA_SINKS</c> environment variable.
/// </summary>
public sealed class TaintSinkRegistry
{
    /// <summary>
    /// Whether taint tracking is globally enabled. Controlled by the
    /// <c>TAINT_TRACKING_ENABLED</c> environment variable (default: <c>true</c>).
    /// When <c>false</c>, no checks or recordings are performed.
    /// </summary>
    public bool IsEnabled { get; }

    // Tool name → bitwise OR of taint labels that must NOT appear in arguments
    // derived from previous tool results.
    private readonly Dictionary<string, TaintLabel> _sinks;

    /// <summary>
    /// Initializes a <see cref="TaintSinkRegistry"/> with the default production sink rules.
    /// </summary>
    public TaintSinkRegistry() : this(isEnabled: true, extraSinks: null) { }

    /// <summary>
    /// Initializes a <see cref="TaintSinkRegistry"/> with explicit enablement and optional
    /// additional sinks. Used by <see cref="FromEnvironment"/> and tests.
    /// </summary>
    /// <param name="isEnabled">Whether taint checking is active.</param>
    /// <param name="extraSinks">
    /// Additional sink rules to merge with the defaults.
    /// Keys are tool names; values are the forbidden label bitmask.
    /// </param>
    internal TaintSinkRegistry(bool isEnabled, IDictionary<string, TaintLabel>? extraSinks)
    {
        IsEnabled = isEnabled;
        _sinks = BuildDefaults();
        if (extraSinks is not null)
            foreach (var (tool, label) in extraSinks)
                _sinks[tool] = _sinks.TryGetValue(tool, out var existing)
                    ? existing | label
                    : label;
    }

    /// <summary>
    /// Creates a <see cref="TaintSinkRegistry"/> configured from environment variables.
    /// Reads <c>TAINT_TRACKING_ENABLED</c> (default true) and
    /// <c>TAINT_EXTRA_SINKS</c> (format: <c>toolName:Label,toolName2:Label2</c>).
    /// </summary>
    /// <returns>A registry instance reflecting the current environment configuration.</returns>
    public static TaintSinkRegistry FromEnvironment()
    {
        var enabledStr = Environment.GetEnvironmentVariable("TAINT_TRACKING_ENABLED");
        var isEnabled = !string.Equals(enabledStr, "false", StringComparison.OrdinalIgnoreCase);

        var extraSinksStr = Environment.GetEnvironmentVariable("TAINT_EXTRA_SINKS");
        var extras = ParseExtraSinks(extraSinksStr);

        return new TaintSinkRegistry(isEnabled, extras.Count > 0 ? extras : null);
    }

    /// <summary>
    /// Returns the set of taint labels that are forbidden from flowing into the given tool.
    /// Returns <see cref="TaintLabel.None"/> if the tool has no sink restrictions.
    /// </summary>
    /// <param name="toolName">Name of the tool to look up.</param>
    public TaintLabel GetForbiddenLabels(string toolName)
        => _sinks.TryGetValue(toolName, out var label) ? label : TaintLabel.None;

    /// <summary>
    /// Adds or replaces a sink rule for the given tool.
    /// Used for runtime extension of the default configuration.
    /// </summary>
    /// <param name="toolName">Name of the tool to configure as a sink.</param>
    /// <param name="forbiddenLabels">Taint labels that must not flow into this tool.</param>
    public void AddSink(string toolName, TaintLabel forbiddenLabels)
        => _sinks[toolName] = forbiddenLabels;

    // ── Private helpers ───────────────────────────────────────────────────────

    private static Dictionary<string, TaintLabel> BuildDefaults() => new()
    {
        // Shell execution: no web content or clone output → prevents code injection.
        ["shell_execute"]            = TaintLabel.ExternalNetwork | TaintLabel.UntrustedAgent,

        // Python interpreter: same rule as shell_execute.
        ["python_code_interpreter"]  = TaintLabel.ExternalNetwork | TaintLabel.UntrustedAgent,

        // Credential store: prevent external sources from overwriting secrets.
        ["manage_credentials"]       = TaintLabel.ExternalNetwork | TaintLabel.UntrustedAgent,

        // Memory write: prevent unfiltered web content from poisoning future pipeline runs.
        ["manage_memory"]            = TaintLabel.UntrustedAgent,

        // HTTP requests: PII and secrets must not appear in external URLs or bodies.
        ["http_request"]             = TaintLabel.Secret | TaintLabel.Pii,
    };

    private static Dictionary<string, TaintLabel> ParseExtraSinks(string? raw)
    {
        var result = new Dictionary<string, TaintLabel>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return result;

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colonIdx = part.IndexOf(':');
            if (colonIdx <= 0) continue;

            var toolName = part[..colonIdx].Trim();
            var labelStr = part[(colonIdx + 1)..].Trim();

            if (Enum.TryParse<TaintLabel>(labelStr, ignoreCase: true, out var label))
                result[toolName] = label;
        }

        return result;
    }
}
