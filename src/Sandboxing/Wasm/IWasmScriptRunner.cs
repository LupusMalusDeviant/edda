namespace Edda.Sandboxing.Wasm;

/// <summary>
/// Abstracts the execution of a Python script in a WASM/Pyodide-compatible environment.
/// Allows unit tests to mock WASM execution without a real runtime.
/// </summary>
public interface IWasmScriptRunner
{
    /// <summary>
    /// Executes a Python script with the given stdin JSON input.
    /// </summary>
    /// <param name="scriptContent">UTF-8 Python source code to execute.</param>
    /// <param name="jsonInput">JSON string passed to the script via stdin.</param>
    /// <param name="timeoutSeconds">Maximum execution time in seconds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple of (Stdout, Stderr, ExitCode, TimedOut).
    /// <c>TimedOut</c> is <see langword="true"/> when execution exceeded <paramref name="timeoutSeconds"/>.
    /// </returns>
    Task<(string Stdout, string Stderr, int ExitCode, bool TimedOut)> RunAsync(
        string scriptContent,
        string jsonInput,
        int timeoutSeconds,
        CancellationToken ct);
}
