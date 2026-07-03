using Edda.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Tdk;

/// <summary>
/// Optional startup self-test (F5): when enabled, runs every rule's validator against its
/// <c>validatorFixtures</c> and logs which rules are not verified. Default off — preserving the
/// original startup behavior. When <c>strict</c> and a genuine fixture mismatch is found, startup
/// fails (fail-fast for CI). If no validator could run at all (sandbox unavailable), the check is
/// skipped without failing.
/// </summary>
internal sealed class TdkFixtureSelfTestHostedService : IHostedService
{
    private readonly ITdkFixtureVerifier _verifier;
    private readonly ILogger<TdkFixtureSelfTestHostedService> _logger;
    private readonly bool _enabled;
    private readonly bool _strict;

    /// <summary>Initializes a new <see cref="TdkFixtureSelfTestHostedService"/>.</summary>
    /// <param name="verifier">The fixture verifier.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="enabled">Whether the self-test runs on startup (<c>TDK_FIXTURE_SELFTEST</c>).</param>
    /// <param name="strict">Whether a genuine mismatch fails startup (<c>TDK_FIXTURE_SELFTEST_STRICT</c>).</param>
    public TdkFixtureSelfTestHostedService(
        ITdkFixtureVerifier verifier,
        ILogger<TdkFixtureSelfTestHostedService> logger,
        bool enabled,
        bool strict)
    {
        _verifier = verifier;
        _logger = logger;
        _enabled = enabled;
        _strict = strict;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            _logger.LogDebug("TDK fixture self-test disabled (TDK_FIXTURE_SELFTEST != true)");
            return;
        }

        var report = await _verifier.VerifyAllAsync(cancellationToken).ConfigureAwait(false);

        if (report.TotalCount == 0)
        {
            _logger.LogInformation("TDK fixture self-test: no rules declare fixtures");
            return;
        }

        // Every case being an engine error means no validator could actually run (e.g. NullSandbox /
        // no Docker) — that is not a rule failure, so skip without failing even in strict mode.
        var allCases = report.Rules.SelectMany(r => r.Cases).ToList();
        if (allCases.Count > 0 && allCases.All(c => c.EngineError))
        {
            _logger.LogInformation(
                "TDK fixture self-test skipped: validators could not be executed (sandbox unavailable)");
            return;
        }

        var unverified = report.Rules.Where(r => !r.Verified).ToList();
        if (unverified.Count == 0)
        {
            _logger.LogInformation(
                "TDK fixture self-test: {Verified}/{Total} rules verified", report.VerifiedCount, report.TotalCount);
            return;
        }

        foreach (var rule in unverified)
        {
            var reasons = string.Join("; ", rule.Cases
                .Where(c => !c.Ok)
                .Select(c => $"{c.Kind}#{c.Index}: {c.Detail}"));
            _logger.LogWarning(
                "TDK fixture self-test: rule {RuleId} not verified — {Reasons}", rule.RuleId, reasons);
        }

        // Fail-fast only on a genuine mismatch (a fixture ran and contradicted its expectation),
        // never on pure engine errors.
        var realMismatch = unverified.Any(r => r.Cases.Any(c => !c.Ok && !c.EngineError));
        if (_strict && realMismatch)
        {
            throw new InvalidOperationException(
                $"TDK fixture self-test failed: {unverified.Count} rule(s) not verified " +
                "(TDK_FIXTURE_SELFTEST_STRICT=true).");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
