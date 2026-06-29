namespace Edda.Core.Models;

/// <summary>
/// Output from a sandbox execution of a TDK validator script.
/// </summary>
public sealed record SandboxResult
{
    /// <summary>OS exit code of the validator process. 0 = success.</summary>
    public required int ExitCode { get; init; }

    /// <summary>Standard output captured from the validator script.</summary>
    public required string Stdout { get; init; }

    /// <summary>Standard error captured from the validator script.</summary>
    public required string Stderr { get; init; }

    /// <summary>True if the sandbox was forcibly terminated due to the 10-second timeout.</summary>
    public required bool TimedOut { get; init; }

    /// <summary>True if the script exited with code 0 and did not time out.</summary>
    public bool Success => ExitCode == 0 && !TimedOut;
}
