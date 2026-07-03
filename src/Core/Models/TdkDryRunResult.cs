namespace Edda.Core.Models;

/// <summary>Raw result of a TDK validator dry-run (F6).</summary>
public sealed record TdkDryRunResult
{
    /// <summary>Validator process exit code (-1 when the sandbox itself could not run the script).</summary>
    public required int ExitCode { get; init; }

    /// <summary>True when the run exceeded its time budget.</summary>
    public required bool TimedOut { get; init; }

    /// <summary>Captured standard output.</summary>
    public required string Stdout { get; init; }

    /// <summary>Captured standard error.</summary>
    public required string Stderr { get; init; }

    /// <summary>True when <see cref="Stdout"/> parsed as a valid <c>{pass, violations}</c> document.</summary>
    public bool OutputParsed { get; init; }

    /// <summary>The validator's pass verdict (only meaningful when <see cref="OutputParsed"/>).</summary>
    public bool Pass { get; init; }

    /// <summary>Parsed violations (empty unless <see cref="OutputParsed"/> and the validator reported some).</summary>
    public IReadOnlyList<TdkViolation> Violations { get; init; } = [];
}
