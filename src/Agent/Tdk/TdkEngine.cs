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

    private readonly ISandboxFactory _sandboxFactory;
    private readonly IRuleConfidenceStore _confidenceStore;
    private readonly IRuleFeedbackService? _feedbackService;
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
    public TdkEngine(
        ISandboxFactory sandboxFactory,
        IRuleConfidenceStore confidenceStore,
        ILogger<TdkEngine> logger,
        IRuleFeedbackService? feedbackService = null)
    {
        _sandboxFactory  = sandboxFactory;
        _confidenceStore = confidenceStore;
        _feedbackService = feedbackService;
        _logger          = logger;
    }

    /// <inheritdoc />
    public async Task<TdkResult> ValidateAsync(
        string response,
        IReadOnlyList<KnowledgeRule> rules,
        AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Only rules with a ValidatorScript
        var validatorRules = rules.Where(r => r.ValidatorScript != null).ToList();
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

        foreach (var rule in validatorRules)
        {
            foreach (var block in codeBlocks)
            {
                await ValidateBlockAsync(rule, block, request, allViolations, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (allViolations.Count > 0)
        {
            _logger.LogInformation(
                "TDK: found {ViolationCount} violation(s)", allViolations.Count);
        }

        return allViolations.Count > 0
            ? new TdkResult { HasViolations = true, Violations = allViolations }
            : TdkResult.NoViolations;
    }

    /// <summary>
    /// Records a TDK outcome to both the sliding-window confidence store (F04)
    /// and the long-term feedback service (F32), if configured.
    /// </summary>
    private void RecordTdkOutcome(string ruleId, bool passed, CancellationToken ct)
    {
        _confidenceStore.RecordOutcome(ruleId, passed);
        if (_feedbackService is not null)
            _ = _feedbackService.RecordTdkOutcomeAsync(ruleId, passed, userId: null, ct);
    }

    private async Task ValidateBlockAsync(
        KnowledgeRule rule,
        CodeBlock block,
        AgentRequest request,
        List<TdkViolation> allViolations,
        CancellationToken ct)
    {
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
                _logger.LogWarning(ex,
                    "TDK: sandbox execution threw for rule {RuleId} — skipping rule", rule.Id);
                RecordTdkOutcome(rule.Id, passed: false, ct);
                return;
            }

            if (!result.Success)
            {
                _logger.LogWarning(
                    "TDK: validator crashed for rule {RuleId} (exitCode={ExitCode}, timedOut={TimedOut}): {Stderr}",
                    rule.Id, result.ExitCode, result.TimedOut, result.Stderr);
                RecordTdkOutcome(rule.Id, passed: false, ct);
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
                RecordTdkOutcome(rule.Id, passed: false, ct);
                return;
            }

            if (output is null)
            {
                _logger.LogWarning("TDK: validator returned null output for rule {RuleId} — skipping", rule.Id);
                RecordTdkOutcome(rule.Id, passed: false, ct);
                return;
            }

            RecordTdkOutcome(rule.Id, passed: output.Pass, ct);

            if (!output.Pass)
            {
                foreach (var v in output.Violations)
                {
                    allViolations.Add(new TdkViolation(v.RuleId, v.Message, v.Severity));
                }
            }
        }
    }
}
