using Edda.AKG.Tdk;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Tdk;

/// <summary>F5: startup self-test host — gating, logging and strict fail-fast behavior.</summary>
public class TdkFixtureSelfTestHostedServiceTests
{
    private readonly Mock<ITdkFixtureVerifier> _verifier = new();

    private TdkFixtureSelfTestHostedService Sut(bool enabled, bool strict) => new(
        _verifier.Object, NullLogger<TdkFixtureSelfTestHostedService>.Instance, enabled, strict);

    private static TdkFixtureVerificationReport Report(params TdkFixtureRuleReport[] rules) =>
        new() { Rules = rules };

    private static TdkFixtureRuleReport VerifiedRule() => new()
    {
        RuleId = "ok", HasFixtures = true, Verified = true,
        Cases = [new TdkFixtureCaseResult { Kind = "pass", Index = 0, Ok = true }],
    };

    private static TdkFixtureRuleReport MismatchRule() => new()
    {
        RuleId = "bad", HasFixtures = true, Verified = false,
        Cases = [new TdkFixtureCaseResult { Kind = "fail", Index = 0, Ok = false, Detail = "expected a violation" }],
    };

    private static TdkFixtureRuleReport EngineErrorRule() => new()
    {
        RuleId = "eng", HasFixtures = true, Verified = false,
        Cases = [new TdkFixtureCaseResult { Kind = "pass", Index = 0, Ok = false, EngineError = true, Detail = "n/a" }],
    };

    [Fact]
    public async Task StartAsync_Disabled_DoesNotVerify()
    {
        await Sut(enabled: false, strict: false).StartAsync(CancellationToken.None);

        _verifier.Verify(v => v.VerifyAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_Enabled_RunsVerification()
    {
        _verifier.Setup(v => v.VerifyAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Report(VerifiedRule()));

        await Sut(enabled: true, strict: false).StartAsync(CancellationToken.None);

        _verifier.Verify(v => v.VerifyAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_StrictWithRealMismatch_Throws()
    {
        _verifier.Setup(v => v.VerifyAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Report(MismatchRule()));

        var act = async () => await Sut(enabled: true, strict: true).StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartAsync_StrictButAllEngineErrors_DoesNotThrow()
    {
        // No validator could actually run (e.g. NullSandbox) → skipped, never a failure.
        _verifier.Setup(v => v.VerifyAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Report(EngineErrorRule()));

        var act = async () => await Sut(enabled: true, strict: true).StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_EnabledNonStrict_MismatchDoesNotThrow()
    {
        _verifier.Setup(v => v.VerifyAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Report(MismatchRule()));

        var act = async () => await Sut(enabled: true, strict: false).StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
