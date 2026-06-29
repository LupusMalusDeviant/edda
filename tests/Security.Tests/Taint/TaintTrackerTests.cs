using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.Taint;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Security.Tests.Taint;

/// <summary>
/// Unit tests for <see cref="TaintTracker"/>.
/// Covers label recording, sink enforcement, declassification, combined labels,
/// and state reset.
/// </summary>
public sealed class TaintTrackerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static TaintTracker CreateSut(TaintSinkRegistry? registry = null)
    {
        var auditMock = new Mock<IAuditLog>();
        auditMock
            .Setup(a => a.LogAsync(
                It.IsAny<AuditEvent>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new TaintTracker(
            registry ?? new TaintSinkRegistry(),
            auditMock.Object,
            NullLogger<TaintTracker>.Instance);
    }

    /// <summary>Builds a ToolCall whose first argument embeds the given tool-call ID reference.</summary>
    private static ToolCall CallWithRef(string toolName, string referencedCallId)
        => new()
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Name      = toolName,
            Arguments = new Dictionary<string, object?> { ["input"] = referencedCallId }
        };

    private static ToolCall PlainCall(string toolName)
        => new()
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Name      = toolName,
            Arguments = new Dictionary<string, object?> { ["arg"] = "value" }
        };

    // ── RecordResult ──────────────────────────────────────────────────────────

    [Fact]
    public void RecordResult_ExternalNetwork_StoredUnderCallId()
    {
        var sut = CreateSut();
        sut.RecordResult("call_abc123", TaintLabel.ExternalNetwork);

        // Check that a sink tool referencing this call gets blocked
        var call    = CallWithRef("shell_execute", "call_abc123");
        var verdict = sut.Check(call);

        verdict.IsAllowed.Should().BeFalse();
        verdict.ViolatingLabel.Should().Be(TaintLabel.ExternalNetwork);
    }

    [Fact]
    public void RecordResult_Secret_StoredUnderCallId()
    {
        var sut = CreateSut();
        sut.RecordResult("toolu_secret01", TaintLabel.Secret);

        var call    = CallWithRef("http_request", "toolu_secret01");
        var verdict = sut.Check(call);

        verdict.IsAllowed.Should().BeFalse();
        verdict.ViolatingLabel.Should().Be(TaintLabel.Secret);
    }

    [Fact]
    public void RecordResult_None_DoesNotRecord_NeverBlocks()
    {
        var sut = CreateSut();
        sut.RecordResult("call_noop01", TaintLabel.None);

        // Even for a sensitive sink, no label stored → Allow
        var call    = CallWithRef("shell_execute", "call_noop01");
        var verdict = sut.Check(call);

        verdict.IsAllowed.Should().BeTrue();
    }

    // ── Check ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Check_ExternalNetworkToShell_BlocksExecution()
    {
        var sut    = CreateSut();
        var callId = "call_web01";
        sut.RecordResult(callId, TaintLabel.ExternalNetwork);

        var shellCall = CallWithRef("shell_execute", callId);
        var verdict   = sut.Check(shellCall);

        verdict.IsAllowed.Should().BeFalse();
        verdict.BlockedToolName.Should().Be("shell_execute");
        verdict.Reason.Should().Contain("shell_execute");
        verdict.Reason.Should().Contain("ExternalNetwork");
    }

    [Fact]
    public void Check_UntrustedAgentToShell_BlocksExecution()
    {
        var sut    = CreateSut();
        var callId = "call_clone01";
        sut.RecordResult(callId, TaintLabel.UntrustedAgent);

        var verdict = sut.Check(CallWithRef("shell_execute", callId));

        verdict.IsAllowed.Should().BeFalse();
        verdict.ViolatingLabel.Should().Be(TaintLabel.UntrustedAgent);
    }

    [Fact]
    public void Check_SecretToHttpRequest_BlocksExecution()
    {
        var sut    = CreateSut();
        var callId = "toolu_cred01";
        sut.RecordResult(callId, TaintLabel.Secret);

        var verdict = sut.Check(CallWithRef("http_request", callId));

        verdict.IsAllowed.Should().BeFalse();
        verdict.ViolatingLabel.Should().Be(TaintLabel.Secret);
    }

    [Fact]
    public void Check_NormalFlow_NoTaint_Allows()
    {
        var sut     = CreateSut();
        var verdict = sut.Check(PlainCall("shell_execute"));

        // No previous taint recorded for any referenced ID → Allow
        verdict.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Check_ToolNotInSinks_Allows()
    {
        var sut    = CreateSut();
        var callId = "call_web01";
        sut.RecordResult(callId, TaintLabel.ExternalNetwork);

        // "get_time" is not in any sink → no restriction
        var verdict = sut.Check(CallWithRef("get_time", callId));

        verdict.IsAllowed.Should().BeTrue();
    }

    // ── Combined labels ───────────────────────────────────────────────────────

    [Fact]
    public void Check_CombinedLabels_OR_Logic_BothRestrictiveLabels()
    {
        var sut    = CreateSut();
        var callId = "call_mixed01";
        sut.RecordResult(callId, TaintLabel.ExternalNetwork);
        sut.RecordResult(callId, TaintLabel.UntrustedAgent); // OR'd onto existing

        var verdict = sut.Check(CallWithRef("python_code_interpreter", callId));

        verdict.IsAllowed.Should().BeFalse();
        // Violation is a subset of both labels
        verdict.ViolatingLabel.Should().NotBe(TaintLabel.None);
    }

    // ── Declassify ────────────────────────────────────────────────────────────

    [Fact]
    public void Declassify_AllowsFlowAfterDeclassification()
    {
        var sut    = CreateSut();
        var callId = "call_web99";
        sut.RecordResult(callId, TaintLabel.ExternalNetwork);

        sut.Declassify(callId, "Operator verified content is safe");

        var verdict = sut.Check(CallWithRef("shell_execute", callId));
        verdict.IsAllowed.Should().BeTrue();
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsAllState()
    {
        var sut    = CreateSut();
        var callId = "call_abc999";
        sut.RecordResult(callId, TaintLabel.ExternalNetwork);

        sut.Reset();

        // After reset, the label is gone → Allow even for a sink tool
        var verdict = sut.Check(CallWithRef("shell_execute", callId));
        verdict.IsAllowed.Should().BeTrue();
    }
}
