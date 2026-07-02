using Edda.Agent.Tdk;
using Edda.Core.Models;

namespace Edda.Agent.Tests.Tdk;

public class TdkFeedbackFormatterTests
{
    [Fact]
    public void Format_SingleViolation_ContainsRuleIdAndMessage()
    {
        var violations = new List<TdkViolation>
        {
            new("no-plaintext-secrets", "Plaintext password detected", "error")
        };

        var result = TdkFeedbackFormatter.Format(violations);

        result.Should().Contain("no-plaintext-secrets");
        result.Should().Contain("Plaintext password detected");
        result.Should().Contain("ERROR");
    }

    [Fact]
    public void TdkFeedbackFormatter_MultipleViolations_FormatsAll()
    {
        var violations = new List<TdkViolation>
        {
            new("rule-001", "HTTP URL detected", "warning"),
            new("rule-002", "Use of eval() detected", "error")
        };

        var result = TdkFeedbackFormatter.Format(violations);

        result.Should().Contain("rule-001");
        result.Should().Contain("rule-002");
        result.Should().Contain("HTTP URL detected");
        result.Should().Contain("Use of eval() detected");
    }

    [Fact]
    public void Format_ContainsRevisionInstruction()
    {
        var violations = new List<TdkViolation>
        {
            new("r1", "message", "error")
        };

        var result = TdkFeedbackFormatter.Format(violations);

        result.Should().Contain("Please revise your response");
    }

    [Fact]
    public void Format_ContainsHeaderSection()
    {
        var violations = new List<TdkViolation>
        {
            new("r1", "message", "info")
        };

        var result = TdkFeedbackFormatter.Format(violations);

        result.Should().Contain("Code Review Required");
    }

    [Fact]
    public void Format_ViolationWithLineAndSuggestion_ShowsBoth()
    {
        var violations = new List<TdkViolation>
        {
            new("r1", "Blocking .Result on async call", "error", Line: 42, Suggestion: "await the call instead")
        };

        var result = TdkFeedbackFormatter.Format(violations);

        result.Should().Contain("line 42");
        result.Should().Contain("await the call instead");
    }

    [Fact]
    public void Format_ViolationWithoutLineOrSuggestion_OmitsThem()
    {
        var violations = new List<TdkViolation>
        {
            new("r1", "message", "error")
        };

        var result = TdkFeedbackFormatter.Format(violations);

        result.Should().NotContain("(line ");
        result.Should().NotContain("Suggestion:");
    }

    [Fact]
    public void Format_SortsBySeverity_ErrorsBeforeWarningsBeforeInfo()
    {
        var violations = new List<TdkViolation>
        {
            new("info-rule", "an info", "info"),
            new("warn-rule", "a warning", "warning"),
            new("err-rule", "an error", "error"),
        };

        var result = TdkFeedbackFormatter.Format(violations);

        result.IndexOf("err-rule", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("warn-rule", StringComparison.Ordinal));
        result.IndexOf("warn-rule", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("info-rule", StringComparison.Ordinal));
    }

    [Fact]
    public void Format_IncludesPerSeverityCountSummary()
    {
        var violations = new List<TdkViolation>
        {
            new("r1", "e1", "error"),
            new("r2", "e2", "error"),
            new("r3", "w1", "warning"),
        };

        var result = TdkFeedbackFormatter.Format(violations);

        result.Should().Contain("3 violation(s)");
        result.Should().Contain("2 error");
        result.Should().Contain("1 warning");
        result.Should().Contain("0 info");
    }

    [Fact]
    public void FormatEngineErrors_ListsFailedValidators()
    {
        var errors = new List<TdkEngineError>
        {
            new("r1", "validator exited with an error", ExitCode: 127),
            new("r2", "validator timed out", TimedOut: true)
        };

        var result = TdkFeedbackFormatter.FormatEngineErrors(errors);

        result.Should().Contain("could not run");
        result.Should().Contain("r1");
        result.Should().Contain("r2");
        result.Should().Contain("validator exited with an error");
        result.Should().Contain("validator timed out");
    }
}
