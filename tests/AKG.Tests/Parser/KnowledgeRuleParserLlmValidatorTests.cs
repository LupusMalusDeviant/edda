using Edda.AKG.Parser;

namespace Edda.AKG.Tests.Parser;

/// <summary>F16: parsing of <c>validatorType: llm</c> + <c>validatorPrompt</c> frontmatter.</summary>
public class KnowledgeRuleParserLlmValidatorTests
{
    private readonly KnowledgeRuleParser _parser = new();

    [Fact]
    public void Parse_ValidatorTypeLlmWithPrompt_PopulatesBoth()
    {
        var rule = _parser.Parse(
            """
            ---
            id: llm-rule
            title: Actionable Errors
            domain: coding
            validatorType: llm
            validatorPrompt: |
              Error messages must be actionable:
              they name the cause and the next step.
            ---
            Body.
            """);

        rule.ValidatorType.Should().Be("llm");
        rule.ValidatorPrompt.Should().Be(
            "Error messages must be actionable:\nthey name the cause and the next step.");
        rule.ValidatorScript.Should().BeNull();
    }

    [Fact]
    public void Parse_NoValidatorType_LeavesBothNull()
    {
        var rule = _parser.Parse(
            """
            ---
            id: plain-rule
            title: Plain
            domain: coding
            ---
            Body.
            """);

        rule.ValidatorType.Should().BeNull();
        rule.ValidatorPrompt.Should().BeNull();
    }
}
