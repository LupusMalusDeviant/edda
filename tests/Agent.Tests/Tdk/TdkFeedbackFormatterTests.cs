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
}
