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

    /// <summary>F4: the bundled tdk.py helper, delivered next to every validator script so scripts
    /// may import it. Harmless for raw stdin/stdout validators that never import it.</summary>
    private readonly IReadOnlyDictionary<string, string> _helperFiles;

    /// <summary>F11: when true, all (rule × block) validators run in a single sandbox via the batch
    /// runner instead of one sandbox per pair. Default false preserves the per-pair behavior.</summary>
    private readonly bool _batchEnabled;

    /// <summary>F16: optional LLM judge for <c>validatorType: llm</c> rules. Null = feature off —
    /// llm rules are skipped with a debug log (no engine error, no confidence outcome).</summary>
    private readonly ITdkLlmJudge? _llmJudge;

    /// <summary>
    /// Initializes a new <see cref="TdkEngine"/>.
    /// </summary>
    /// <param name="sandboxFactory">Creates isolated sandboxes for executing validator scripts.</param>
    /// <param name="confidenceStore">Records pass/fail outcomes to adjust rule weights over time.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="helper">
    /// F4 helper module (<c>tdk.py</c>) delivered next to every validator script so validators can
    /// import shared JSON-I/O and helpers. Raw stdin/stdout validators are unaffected.
    /// </param>
    /// <param name="feedbackService">
    /// Optional F32 feedback service. When provided, TDK outcomes are also forwarded
    /// for use in long-term confidence multiplier calculations.
    /// </param>
    /// <param name="resultCache">
    /// Optional F13 result cache. When provided, the outcome of an identical (rule × validator × block)
    /// validation is reused instead of re-running the sandbox. Null disables caching.
    /// </param>
    /// <param name="batchEnabled">
    /// F11: when true, all (rule × block) validators run in a single sandbox (batch runner) instead of
    /// one sandbox per pair. Defaults to false — the per-pair behavior, unchanged.
    /// </param>
    /// <param name="llmJudge">
    /// F16: optional LLM judge for <c>validatorType: llm</c> rules. Null (the default) disables the
    /// feature — llm rules are skipped silently. Registered only when <c>TDK_LLM_JUDGE=true</c>.
    /// </param>
    public TdkEngine(
        ISandboxFactory sandboxFactory,
        IRuleConfidenceStore confidenceStore,
        ILogger<TdkEngine> logger,
        ITdkHelperModule helper,
        IRuleFeedbackService? feedbackService = null,
        ITdkResultCache? resultCache = null,
        bool batchEnabled = false,
        ITdkLlmJudge? llmJudge = null)
    {
        _sandboxFactory  = sandboxFactory;
        _confidenceStore = confidenceStore;
        _feedbackService = feedbackService;
        _resultCache     = resultCache;
        _logger          = logger;
        _helperFiles     = new Dictionary<string, string>(1) { [helper.FileName] = helper.Source };
        _batchEnabled    = batchEnabled;
        _llmJudge        = llmJudge;
    }

    /// <inheritdoc />
    public async Task<TdkResult> ValidateAsync(
        string response,
        IReadOnlyList<KnowledgeRule> rules,
        AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Only enabled validators run (F7 kill-switch): script rules carry a ValidatorScript;
        //    llm rules (F16) carry validatorType: llm plus a ValidatorPrompt.
        var validatorRules = rules.Where(r =>
            r.ValidatorEnabled && (r.ValidatorScript != null || IsLlmRule(r))).ToList();
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

        // F16: llm-judge rules never run in the sandbox — they are judged separately (in both the
        // per-pair and the batch mode), before the script validators.
        var llmRules = validatorRules.Where(IsLlmRule).ToList();
        var scriptRules = validatorRules.Where(r => !IsLlmRule(r)).ToList();

        var allViolations = new List<TdkViolation>();
        var engineErrors = new List<TdkEngineError>();

        await JudgeLlmRulesAsync(llmRules, codeBlocks, allViolations, engineErrors, cancellationToken)
            .ConfigureAwait(false);

        // F11: opt-in batch mode runs every script (rule × block) pair in a single sandbox.
        if (_batchEnabled)
            return await BatchValidateAsync(
                scriptRules, codeBlocks, request, allViolations, engineErrors, cancellationToken)
                .ConfigureAwait(false);

        // 3. Validate each script (rule × code block) combination
        foreach (var rule in scriptRules)
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

        return BuildResult(allViolations, engineErrors);
    }

    /// <summary>Builds a <see cref="TdkResult"/> from collected violations and engine errors.</summary>
    private static TdkResult BuildResult(List<TdkViolation> violations, List<TdkEngineError> engineErrors)
        => violations.Count == 0 && engineErrors.Count == 0
            ? TdkResult.NoViolations
            : new TdkResult
            {
                HasViolations = violations.Count > 0,
                Violations = violations,
                EngineErrors = engineErrors,
            };

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
                    _helperFiles,
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

    /// <summary>
    /// F11 batch path: runs every eligible (rule × block) validator in a single sandbox via the batch
    /// runner. Applies the same per-job semantics as <see cref="ValidateBlockAsync"/> — F9 language
    /// filter and F13 cache are honored before batching; a confidence outcome is recorded only for a
    /// real verdict; sandbox/validator failures become per-job engine errors (never confidence).
    /// </summary>
    private async Task<TdkResult> BatchValidateAsync(
        IReadOnlyList<KnowledgeRule> validatorRules,
        IReadOnlyList<CodeBlock> codeBlocks,
        AgentRequest request,
        List<TdkViolation> allViolations,
        List<TdkEngineError> engineErrors,
        CancellationToken ct)
    {
        var jobs = new List<TdkBatchJob>();
        var meta = new List<(KnowledgeRule Rule, string? CacheKey)>();

        // Collect jobs, honoring F9 (language) and F13 (cache) before any sandbox is created.
        foreach (var rule in validatorRules)
        {
            foreach (var block in codeBlocks)
            {
                if (!TdkLanguageMatcher.Applies(rule.AppliesTo, block.Language))
                    continue;

                var cacheKey = _resultCache is null
                    ? null
                    : TdkResultCacheKey.Compute(rule.Id, rule.ValidatorScript!, block.Language, block.Code);
                if (cacheKey is not null && _resultCache!.Get(cacheKey) is { } cached)
                {
                    if (!cached.Pass)
                        allViolations.AddRange(cached.Violations);
                    continue;
                }

                jobs.Add(new TdkBatchJob
                {
                    Id = meta.Count,
                    Script = rule.ValidatorScript!,
                    Input = new TdkValidatorInput
                    {
                        Code = block.Code,
                        Language = block.Language,
                        RuleId = rule.Id,
                        UserMessage = request.UserMessage,
                    },
                });
                meta.Add((rule, cacheKey));
            }
        }

        if (jobs.Count == 0)
            return BuildResult(allViolations, engineErrors);

        // One sandbox for the whole batch — the runner executes each job as a subprocess.
        SandboxResult sandboxResult;
        try
        {
            await using var sandbox = await _sandboxFactory.CreateAsync(ct).ConfigureAwait(false);
            sandboxResult = await sandbox.ExecuteAsync(
                TdkBatchRunner.Source,
                JsonSerializer.Serialize(new TdkBatchRequest { Jobs = jobs }),
                _helperFiles,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "TDK batch: sandbox execution failed");
            foreach (var (rule, _) in meta)
                engineErrors.Add(new TdkEngineError(rule.Id, "batch sandbox execution failed"));
            return BuildResult(allViolations, engineErrors);
        }

        if (!sandboxResult.Success)
        {
            foreach (var (rule, _) in meta)
                engineErrors.Add(new TdkEngineError(
                    rule.Id,
                    sandboxResult.TimedOut ? "batch runner timed out" : "batch runner exited with an error",
                    sandboxResult.ExitCode,
                    Truncate(sandboxResult.Stderr),
                    sandboxResult.TimedOut));
            return BuildResult(allViolations, engineErrors);
        }

        TdkBatchResponse? batch;
        try
        {
            batch = JsonSerializer.Deserialize<TdkBatchResponse>(sandboxResult.Stdout, JsonOptions);
        }
        catch (JsonException)
        {
            batch = null;
        }

        if (batch is null)
        {
            foreach (var (rule, _) in meta)
                engineErrors.Add(new TdkEngineError(rule.Id, "batch runner returned invalid JSON"));
            return BuildResult(allViolations, engineErrors);
        }

        var byId = new Dictionary<int, TdkBatchJobResult>();
        foreach (var jobResult in batch.Results)
            byId[jobResult.Id] = jobResult;

        for (var id = 0; id < meta.Count; id++)
        {
            var (rule, cacheKey) = meta[id];
            if (!byId.TryGetValue(id, out var jobResult))
            {
                engineErrors.Add(new TdkEngineError(rule.Id, "batch runner produced no result for this job"));
                continue;
            }

            if (jobResult.ExitCode != 0)
            {
                engineErrors.Add(new TdkEngineError(
                    rule.Id,
                    jobResult.TimedOut ? "validator timed out" : "validator exited with an error",
                    jobResult.ExitCode,
                    Truncate(jobResult.Stderr),
                    jobResult.TimedOut));
                continue;
            }

            TdkValidatorOutput? output;
            try
            {
                output = JsonSerializer.Deserialize<TdkValidatorOutput>(jobResult.Stdout, JsonOptions);
            }
            catch (JsonException)
            {
                engineErrors.Add(new TdkEngineError(rule.Id, "validator returned invalid JSON"));
                continue;
            }

            if (output is null)
            {
                engineErrors.Add(new TdkEngineError(rule.Id, "validator returned no output"));
                continue;
            }

            RecordTdkOutcome(rule.Id, passed: output.Pass, ValidatorScriptHash.Compute(rule.ValidatorScript), ct);

            var violations = output.Violations
                .Select(v => new TdkViolation(v.RuleId, v.Message, v.Severity, v.Line, v.Suggestion))
                .ToList();

            if (cacheKey is not null)
                _resultCache?.Set(cacheKey, new TdkCachedOutcome(output.Pass, violations));

            if (!output.Pass)
                allViolations.AddRange(violations);
        }

        return BuildResult(allViolations, engineErrors);
    }

    /// <summary>Whether the rule is an F16 llm-judge rule (validatorType: llm plus a prompt).</summary>
    private static bool IsLlmRule(KnowledgeRule rule)
        => string.Equals(rule.ValidatorType, "llm", StringComparison.OrdinalIgnoreCase)
           && rule.ValidatorPrompt != null;

    /// <summary>
    /// F16: judges every (llm rule × block) pair via the optional LLM judge. Honors the F9 language
    /// filter and the F13 cache (prompt as the validator source); a real verdict records a confidence
    /// outcome (the sliding window automatically devalues an unreliable judge), while judge failures
    /// become engine errors and never touch confidence. Without a registered judge, llm rules are
    /// skipped silently — off is off.
    /// </summary>
    private async Task JudgeLlmRulesAsync(
        IReadOnlyList<KnowledgeRule> llmRules,
        IReadOnlyList<CodeBlock> codeBlocks,
        List<TdkViolation> allViolations,
        List<TdkEngineError> engineErrors,
        CancellationToken ct)
    {
        if (llmRules.Count == 0)
            return;

        if (_llmJudge is null)
        {
            _logger.LogDebug(
                "TDK: {Count} llm-judge rule(s) present but TDK_LLM_JUDGE is not enabled — skipping",
                llmRules.Count);
            return;
        }

        foreach (var rule in llmRules)
        {
            foreach (var block in codeBlocks)
            {
                if (!TdkLanguageMatcher.Applies(rule.AppliesTo, block.Language))
                    continue;

                var cacheKey = _resultCache is null
                    ? null
                    : TdkResultCacheKey.Compute(rule.Id, rule.ValidatorPrompt!, block.Language, block.Code);
                if (cacheKey is not null && _resultCache!.Get(cacheKey) is { } cached)
                {
                    if (!cached.Pass)
                        allViolations.AddRange(cached.Violations);
                    continue;
                }

                TdkJudgeResult verdict;
                try
                {
                    verdict = await _llmJudge.JudgeAsync(new TdkJudgeRequest
                    {
                        RuleId = rule.Id,
                        Prompt = rule.ValidatorPrompt!,
                        Code = block.Code,
                        Language = block.Language,
                    }, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "TDK: llm judge threw for rule {RuleId}", rule.Id);
                    engineErrors.Add(new TdkEngineError(rule.Id, "llm judge threw: " + ex.Message));
                    continue;
                }

                if (!verdict.Executed)
                {
                    engineErrors.Add(new TdkEngineError(
                        rule.Id, "llm judge failed: " + (verdict.Error ?? "unknown")));
                    continue;
                }

                RecordTdkOutcome(
                    rule.Id, passed: verdict.Pass, ValidatorScriptHash.Compute(rule.ValidatorPrompt), ct);

                if (cacheKey is not null)
                    _resultCache?.Set(cacheKey, new TdkCachedOutcome(verdict.Pass, verdict.Violations));

                if (!verdict.Pass)
                    allViolations.AddRange(verdict.Violations);
            }
        }
    }
}
