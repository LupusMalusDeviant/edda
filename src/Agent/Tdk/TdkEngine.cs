using System.Text.Json;
using Edda.Agent.Tdk.Models;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tdk;

/// <summary>
/// Production implementation of <see cref="ITdkEngine"/>.
/// Validates code blocks in agent responses against Python validator scripts
/// attached to AKG rules, using an <see cref="ISandboxFactory"/> for isolation.
/// </summary>
public sealed class TdkEngine : ITdkEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Maximum length of a validator stderr excerpt kept in an engine-error report.</summary>
    private const int MaxStderrExcerpt = 500;

    private readonly ISandboxFactory _sandboxFactory;
    private readonly IRuleConfidenceStore _confidenceStore;
    private readonly IRuleFeedbackService? _feedbackService;
    private readonly ITdkResultCache? _resultCache;
    private readonly ILogger<TdkEngine> _logger;

    /// <summary>
    /// Initializes a new <see cref="TdkEngine"/>.
    /// </summary>
    /// <param name="sandboxFactory">Creates isolated sandboxes for executing validator scripts.</param>
    /// <param name="confidenceStore">Records pass/fail outcomes to adjust rule weights over time.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="feedbackService">
    /// Optional F32 feedback service. When provided, TDK outcomes are also forwarded
    /// for use in long-term confidence multiplier calculations.
    /// </param>
    /// <param name="resultCache">
    /// Optional F13 result cache. When provided, the outcome of an identical (rule × validator × block)
    /// validation is reused instead of re-running the sandbox. Null disables caching.
    /// </param>
    public TdkEngine(
        ISandboxFactory sandboxFactory,
        IRuleConfidenceStore confidenceStore,
        ILogger<TdkEngine> logger,
        IRuleFeedbackService? feedbackService = null,
        ITdkResultCache? resultCache = null)
    {
        _sandboxFactory  = sandboxFactory;
        _confidenceStore = confidenceStore;
        _feedbackService = feedbackService;
        _resultCache     = resultCache;
        _logger          = logger;
    }

    /// <inheritdoc />
    public async Task<TdkResult> ValidateAsync(
        string response,
        IReadOnlyList<KnowledgeRule> rules,
        AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Only rules with a ValidatorScript whose validator is enabled (F7 kill-switch: a disabled
        //    validator stays in the graph but does not run).
        var validatorRules = rules.Where(r => r.ValidatorScript != null && r.ValidatorEnabled).ToList();
        if (validatorRules.Count == 0)
        {
            _logger.LogDebug("TDK: no validator rules active — skipping validation");
            return TdkResult.NoViolations;
        }

        // 2. Extract code blocks from the response
        var codeBlocks = CodeBlockExtractor.Extract(response);
        if (codeBlocks.Count == 0)
        {
            _logger.LogDebug("TDK: no code blocks found — skipping validation");
            return TdkResult.NoViolations;
        }

        _logger.LogInformation(
            "TDK: validating {BlockCount} code block(s) against {RuleCount} rule(s)",
            codeBlocks.Count, validatorRules.Count);

        // 3. Validate each (rule × code block) combination
        var allViolations = new List<TdkViolation>();
        var engineErrors = new List<TdkEngineError>();

        foreach (var rule in validatorRules)
        {
            foreach (var block in codeBlocks)
            {
                // F9: skip a (rule × block) pair whose block language the rule does not target — before any
                // sandbox is created. Saves a container run and avoids cross-language false positives from a
                // language-specific validator matching a foreign block.
                if (!TdkLanguageMatcher.Applies(rule.AppliesTo, block.Language))
                {
                    _logger.LogDebug(
                        "TDK: rule {RuleId} does not target '{Language}' blocks — skipping before sandbox",
                        rule.Id, block.Language);
                    continue;
                }

                await ValidateBlockAsync(rule, block, request, allViolations, engineErrors, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (allViolations.Count > 0)
        {
            _logger.LogInformation(
                "TDK: found {ViolationCount} violation(s)", allViolations.Count);
        }

        if (engineErrors.Count > 0)
        {
            _logger.LogWarning(
                "TDK: {ErrorCount} validator(s) could not be executed", engineErrors.Count);
        }

        return allViolations.Count == 0 && engineErrors.Count == 0
            ? TdkResult.NoViolations
            : new TdkResult
            {
                HasViolations = allViolations.Count > 0,
                Violations = allViolations,
                EngineErrors = engineErrors,
            };
    }

    /// <summary>
    /// Records a TDK outcome to both the sliding-window confidence store (F04)
    /// and the long-term feedback service (F32), if configured.
    /// </summary>
    private void RecordTdkOutcome(string ruleId, bool passed, string? validatorHash, CancellationToken ct)
    {
        // F7: the hash lets the confidence store reset a rule's window when its validator script changes.
        _confidenceStore.RecordOutcome(ruleId, passed, validatorHash);
        if (_feedbackService is not null)
            _ = _feedbackService.RecordTdkOutcomeAsync(ruleId, passed, userId: null, ct);
    }

    /// <summary>Truncates a validator's stderr to a short excerpt for the engine-error report.</summary>
    private static string? Truncate(string? text)
        => string.IsNullOrEmpty(text) || text.Length <= MaxStderrExcerpt
            ? text
            : text[..MaxStderrExcerpt] + "…";

    private async Task ValidateBlockAsync(
        KnowledgeRule rule,
        CodeBlock block,
        AgentRequest request,
        List<TdkViolation> allViolations,
        List<TdkEngineError> engineErrors,
        CancellationToken ct)
    {
        // F13: reuse a previously computed outcome for the identical (rule × validator × block) tuple and
        // skip the sandbox entirely. The confidence store is intentionally NOT re-recorded on a hit: no
        // validator ran, and re-recording identical outcomes in an agent re-validation loop would flood it.
        var cacheKey = _resultCache is null
            ? null
            : TdkResultCacheKey.Compute(rule.Id, rule.ValidatorScript!, block.Language, block.Code);
        if (cacheKey is not null && _resultCache!.Get(cacheKey) is { } cached)
        {
            _logger.LogDebug("TDK: cache hit for rule {RuleId} — reusing outcome, skipping sandbox", rule.Id);
            if (!cached.Pass)
                allViolations.AddRange(cached.Violations);
            return;
        }

        ISandbox? sandbox = null;
        try
        {
            sandbox = await _sandboxFactory.CreateAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TDK: failed to create sandbox for rule {RuleId} — propagating", rule.Id);
            throw;
        }

        await using (sandbox)
        {
            var input = new TdkValidatorInput
            {
                Code = block.Code,
                Language = block.Language,
                RuleId = rule.Id,
                UserMessage = request.UserMessage
            };

            SandboxResult result;
            try
            {
                result = await sandbox.ExecuteAsync(
                    rule.ValidatorScript!,
                    JsonSerializer.Serialize(input),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Infrastructure failure (sandbox threw) — surface it, but do NOT record a pass/fail
                // outcome: an engine error is not a business result and must not skew rule confidence.
                _logger.LogWarning(ex,
                    "TDK: sandbox execution threw for rule {RuleId} — skipping rule", rule.Id);
                engineErrors.Add(new TdkEngineError(rule.Id, "sandbox execution failed"));
                return;
            }

            if (!result.Success)
            {
                _logger.LogWarning(
                    "TDK: validator crashed for rule {RuleId} (exitCode={ExitCode}, timedOut={TimedOut}): {Stderr}",
                    rule.Id, result.ExitCode, result.TimedOut, result.Stderr);
                engineErrors.Add(new TdkEngineError(
                    rule.Id,
                    result.TimedOut ? "validator timed out" : "validator exited with an error",
                    result.ExitCode,
                    Truncate(result.Stderr),
                    result.TimedOut));
                return;
            }

            TdkValidatorOutput? output;
            try
            {
                output = JsonSerializer.Deserialize<TdkValidatorOutput>(result.Stdout, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "TDK: validator returned invalid JSON for rule {RuleId} — skipping", rule.Id);
                engineErrors.Add(new TdkEngineError(rule.Id, "validator returned invalid JSON"));
                return;
            }

            if (output is null)
            {
                _logger.LogWarning("TDK: validator returned null output for rule {RuleId} — skipping", rule.Id);
                engineErrors.Add(new TdkEngineError(rule.Id, "validator returned no output"));
                return;
            }

            // Only a validator that actually ran and produced a verdict records a pass/fail outcome.
            RecordTdkOutcome(rule.Id, passed: output.Pass, ValidatorScriptHash.Compute(rule.ValidatorScript), ct);

            var violations = output.Violations
                .Select(v => new TdkViolation(v.RuleId, v.Message, v.Severity, v.Line, v.Suggestion))
                .ToList();

            // F13: cache this real validator outcome so an identical re-validation reuses it. Engine errors
            // above return early and are deliberately never cached — they are transient.
            _resultCache?.Set(cacheKey!, new TdkCachedOutcome(output.Pass, violations));

            if (!output.Pass)
                allViolations.AddRange(violations);
        }
    }
}
