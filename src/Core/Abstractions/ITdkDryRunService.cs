using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Executes an arbitrary TDK validator script against arbitrary sample code in the sandbox and
/// returns the raw result (exit code, stdout, stderr, parsed violations). Used by the /tdk dry-run
/// editor (F6) so validator authors get instant feedback without committing a rule. Records no
/// confidence outcome.
/// </summary>
public interface ITdkDryRunService
{
    /// <summary>Runs <paramref name="script"/> against <paramref name="code"/> in the sandbox.</summary>
    /// <param name="script">Python validator source to run.</param>
    /// <param name="code">Sample code passed to the validator as the <c>code</c> field.</param>
    /// <param name="language">Optional language identifier for the <c>language</c> field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The captured run result.</returns>
    Task<TdkDryRunResult> RunAsync(
        string script, string code, string? language = null, CancellationToken cancellationToken = default);
}
