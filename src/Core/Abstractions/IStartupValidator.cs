using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Validates system prerequisites at startup before accepting requests.
/// Errors block startup; warnings allow startup with degraded functionality.
/// </summary>
public interface IStartupValidator
{
    /// <summary>
    /// Runs all startup validation checks and aggregates the results.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A validation result containing all detected issues.
    /// IsValid=false means at least one Error-severity issue was found.
    /// </returns>
    Task<ValidationResult> ValidateAsync(CancellationToken ct = default);
}
