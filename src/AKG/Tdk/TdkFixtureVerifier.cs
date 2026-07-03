using System.Text.Json;
using System.Text.Json.Serialization;
using Edda.AKG.Parser;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Tdk;

/// <summary>
/// Verifies a rule's TDK validator against its own <c>validatorFixtures</c> (F5). Runs each fixture
/// snippet through a fresh sandbox (with the F4 <c>tdk.py</c> helper delivered next to the script)
/// and checks the verdict: <c>pass</c> snippets must yield no violations, <c>fail</c> snippets at
/// least one. Never touches the confidence store — a self-test must not skew rule weights.
/// </summary>
internal sealed class TdkFixtureVerifier : ITdkFixtureVerifier
{
    private const int MaxDetailChars = 300;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IFileSystem _fileSystem;
    private readonly ISandboxFactory _sandboxFactory;
    private readonly IReadOnlyDictionary<string, string> _helperFiles;
    private readonly ILogger<TdkFixtureVerifier> _logger;
    private readonly string _knowledgeDirectory;

    /// <summary>Initializes a new <see cref="TdkFixtureVerifier"/>.</summary>
    /// <param name="fileSystem">File system abstraction for reading rule Markdown.</param>
    /// <param name="sandboxFactory">Creates sandboxes for executing validator scripts.</param>
    /// <param name="helper">The F4 helper module delivered next to each validator script.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="knowledgeDirectory">Directory scanned by <see cref="VerifyAllAsync"/>.</param>
    public TdkFixtureVerifier(
        IFileSystem fileSystem,
        ISandboxFactory sandboxFactory,
        ITdkHelperModule helper,
        ILogger<TdkFixtureVerifier> logger,
        string knowledgeDirectory)
    {
        _fileSystem = fileSystem;
        _sandboxFactory = sandboxFactory;
        _helperFiles = new Dictionary<string, string>(1) { [helper.FileName] = helper.Source };
        _logger = logger;
        _knowledgeDirectory = knowledgeDirectory;
    }

    /// <inheritdoc />
    public async Task<TdkFixtureVerificationReport> VerifyAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.DirectoryExists(_knowledgeDirectory))
        {
            _logger.LogWarning(
                "TDK fixture verify: knowledge directory '{Directory}' does not exist", _knowledgeDirectory);
            return new TdkFixtureVerificationReport();
        }

        var parser = new KnowledgeRuleParser();
        var reports = new List<TdkFixtureRuleReport>();

        foreach (var file in _fileSystem.EnumerateFiles(_knowledgeDirectory, "*.md", recursive: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            KnowledgeRule rule;
            try
            {
                var content = await _fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                rule = parser.Parse(content);
            }
            catch (RuleParseException ex)
            {
                _logger.LogWarning(ex, "TDK fixture verify: skipping unparseable rule '{File}'", file);
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "TDK fixture verify: failed to read rule '{File}'", file);
                continue;
            }

            if (rule.ValidatorFixtures is null)
                continue;

            reports.Add(await VerifyRuleAsync(rule, cancellationToken).ConfigureAwait(false));
        }

        return new TdkFixtureVerificationReport { Rules = reports };
    }

    /// <inheritdoc />
    public async Task<TdkFixtureRuleReport> VerifyRuleAsync(
        KnowledgeRule rule, CancellationToken cancellationToken = default)
    {
        if (rule.ValidatorScript is null || rule.ValidatorFixtures is null)
            return new TdkFixtureRuleReport { RuleId = rule.Id, HasFixtures = false };

        var language = rule.AppliesTo.Count > 0 ? rule.AppliesTo[0] : "";
        var cases = new List<TdkFixtureCaseResult>();

        for (var i = 0; i < rule.ValidatorFixtures.Pass.Count; i++)
            cases.Add(await RunCaseAsync(rule, rule.ValidatorFixtures.Pass[i], "pass", i, language, cancellationToken)
                .ConfigureAwait(false));

        for (var i = 0; i < rule.ValidatorFixtures.Fail.Count; i++)
            cases.Add(await RunCaseAsync(rule, rule.ValidatorFixtures.Fail[i], "fail", i, language, cancellationToken)
                .ConfigureAwait(false));

        return new TdkFixtureRuleReport
        {
            RuleId = rule.Id,
            HasFixtures = true,
            Verified = cases.All(c => c.Ok),
            Cases = cases,
        };
    }

    private async Task<TdkFixtureCaseResult> RunCaseAsync(
        KnowledgeRule rule, string snippet, string kind, int index, string language, CancellationToken ct)
    {
        var inputJson = JsonSerializer.Serialize(new
        {
            code = snippet,
            language,
            rule_id = rule.Id,
            user_message = "",
        });

        SandboxResult result;
        try
        {
            await using var sandbox = await _sandboxFactory.CreateAsync(ct).ConfigureAwait(false);
            result = await sandbox.ExecuteAsync(rule.ValidatorScript!, inputJson, _helperFiles, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EngineError(kind, index, "sandbox execution failed: " + Truncate(ex.Message));
        }

        if (!result.Success)
        {
            var detail = result.TimedOut
                ? "validator timed out"
                : $"validator exited with code {result.ExitCode}: {Truncate(result.Stderr)}";
            return EngineError(kind, index, detail);
        }

        FixtureOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<FixtureOutput>(result.Stdout, JsonOptions);
        }
        catch (JsonException)
        {
            return EngineError(kind, index, "validator returned invalid JSON");
        }

        if (output is null)
            return EngineError(kind, index, "validator returned no output");

        // pass-fixture must yield no violations; fail-fixture must yield at least one.
        var ok = kind == "pass" ? output.Pass : !output.Pass;
        var detailText = ok
            ? null
            : kind == "pass" ? "expected no violations, but the validator reported one"
                             : "expected at least one violation, but the validator reported none";

        return new TdkFixtureCaseResult { Kind = kind, Index = index, Ok = ok, Detail = detailText };
    }

    private static TdkFixtureCaseResult EngineError(string kind, int index, string detail)
        => new() { Kind = kind, Index = index, Ok = false, EngineError = true, Detail = detail };

    private static string Truncate(string? text)
        => string.IsNullOrEmpty(text) || text.Length <= MaxDetailChars
            ? text ?? string.Empty
            : text[..MaxDetailChars] + "…";

    /// <summary>Minimal projection of the validator's stdout — only the pass/fail verdict is needed.</summary>
    private sealed record FixtureOutput
    {
        [JsonPropertyName("pass")]
        public bool Pass { get; init; }
    }
}
