using System.Text.Json;
using Edda.Agent.Tdk.Models;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tdk;

/// <summary>
/// Executes an arbitrary validator script against arbitrary sample code in the sandbox for the /tdk
/// dry-run editor (F6). Delivers the F4 <c>tdk.py</c> helper next to the script and returns the raw
/// exit code, stdout, stderr and — when the stdout is a valid validator document — the parsed
/// violations. Records no confidence outcome and persists nothing.
/// </summary>
internal sealed class TdkDryRunService : ITdkDryRunService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ISandboxFactory _sandboxFactory;
    private readonly IReadOnlyDictionary<string, string> _helperFiles;
    private readonly ILogger<TdkDryRunService> _logger;

    /// <summary>Initializes a new <see cref="TdkDryRunService"/>.</summary>
    /// <param name="sandboxFactory">Creates sandboxes for executing the script.</param>
    /// <param name="helper">The F4 helper module delivered next to the script.</param>
    /// <param name="logger">Structured logger.</param>
    public TdkDryRunService(
        ISandboxFactory sandboxFactory,
        ITdkHelperModule helper,
        ILogger<TdkDryRunService> logger)
    {
        _sandboxFactory = sandboxFactory;
        _helperFiles = new Dictionary<string, string>(1) { [helper.FileName] = helper.Source };
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TdkDryRunResult> RunAsync(
        string script, string code, string? language = null, CancellationToken cancellationToken = default)
    {
        var input = new TdkValidatorInput
        {
            Code = code,
            Language = language ?? "",
            RuleId = "dry-run",
            UserMessage = "",
        };

        SandboxResult result;
        try
        {
            await using var sandbox = await _sandboxFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
            result = await sandbox.ExecuteAsync(
                script, JsonSerializer.Serialize(input), _helperFiles, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "TDK dry-run: sandbox execution failed");
            return new TdkDryRunResult
            {
                ExitCode = -1, TimedOut = false, Stdout = string.Empty, Stderr = ex.Message, OutputParsed = false,
            };
        }

        // Try to parse the validator's stdout; a dry-run still shows raw stdout/stderr when it is not valid.
        if (result.Success)
        {
            try
            {
                var output = JsonSerializer.Deserialize<TdkValidatorOutput>(result.Stdout, JsonOptions);
                if (output is not null)
                {
                    var violations = output.Violations
                        .Select(v => new TdkViolation(v.RuleId, v.Message, v.Severity, v.Line, v.Suggestion))
                        .ToList();
                    return new TdkDryRunResult
                    {
                        ExitCode = result.ExitCode,
                        TimedOut = result.TimedOut,
                        Stdout = result.Stdout,
                        Stderr = result.Stderr,
                        OutputParsed = true,
                        Pass = output.Pass,
                        Violations = violations,
                    };
                }
            }
            catch (JsonException)
            {
                // Fall through: surface the raw output so the author can see what the script printed.
            }
        }

        return new TdkDryRunResult
        {
            ExitCode = result.ExitCode,
            TimedOut = result.TimedOut,
            Stdout = result.Stdout,
            Stderr = result.Stderr,
            OutputParsed = false,
        };
    }
}
