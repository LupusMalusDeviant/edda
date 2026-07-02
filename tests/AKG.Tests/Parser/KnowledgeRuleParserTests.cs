using Edda.AKG.Parser;
using Edda.Core.Exceptions;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Parser;

public class KnowledgeRuleParserTests
{
    private readonly KnowledgeRuleParser _parser = new();

    private const string FullMarkdown =
        """
        ---
        id: test-rule-001
        title: Test Rule
        domain: csharp
        priority: High
        type: Rule
        tags: [async, patterns]
        ---
        This is the rule body.
        """;

    [Fact]
    public void Parse_ValidFrontmatter_ReturnsRule()
    {
        var rule = _parser.Parse(FullMarkdown);

        rule.Id.Should().Be("test-rule-001");
        rule.Domain.Should().Be("csharp");
        rule.Priority.Should().Be(RulePriority.High);
        rule.Type.Should().Be("Rule");
        rule.Tags.Should().BeEquivalentTo(new[] { "async", "patterns" });
        rule.Body.Should().Be("This is the rule body.");
    }

    [Fact]
    public void Parse_MissingId_ThrowsRuleParseException()
    {
        var markdown =
            """
            ---
            title: Test Rule
            domain: csharp
            ---
            Body.
            """;

        var act = () => _parser.Parse(markdown);

        act.Should().Throw<RuleParseException>()
            .Which.MissingField.Should().Be("id");
    }

    [Fact]
    public void Parse_MissingTitle_ThrowsRuleParseException()
    {
        var markdown =
            """
            ---
            id: test-rule-001
            domain: csharp
            ---
            Body.
            """;

        var act = () => _parser.Parse(markdown);

        act.Should().Throw<RuleParseException>()
            .Which.MissingField.Should().Be("title");
    }

    [Fact]
    public void Parse_CustomDomain_SetsDomain()
    {
        var markdown =
            """
            ---
            id: governance-rule-001
            title: Governance Rule
            domain: governance
            ---
            Rule body.
            """;

        var rule = _parser.Parse(markdown);

        rule.Domain.Should().Be("governance");
    }

    [Fact]
    public void Parse_InlineTagList_ParsesCorrectly()
    {
        var markdown =
            """
            ---
            id: tag-test
            title: Tag Test
            tags: [async, patterns]
            ---
            Body.
            """;

        var rule = _parser.Parse(markdown);

        rule.Tags.Should().BeEquivalentTo(new[] { "async", "patterns" });
    }

    [Fact]
    public void Parse_MultilineTagList_ParsesCorrectly()
    {
        var markdown =
            """
            ---
            id: multiline-tags
            title: Multiline Tags
            tags:
              - async
              - patterns
              - csharp
            ---
            Body.
            """;

        var rule = _parser.Parse(markdown);

        rule.Tags.Should().BeEquivalentTo(new[] { "async", "patterns", "csharp" });
    }

    [Fact]
    public void Parse_NoPriority_DefaultsMedium()
    {
        var markdown =
            """
            ---
            id: no-priority
            title: No Priority
            domain: general
            ---
            Body.
            """;

        var rule = _parser.Parse(markdown);

        rule.Priority.Should().Be(RulePriority.Medium);
    }

    [Fact]
    public void SplitFrontmatter_NoDashes_ReturnsEmptyFrontmatter()
    {
        var markdown = "This is just body text without any frontmatter.";

        var (frontmatter, body) = KnowledgeRuleParser.SplitFrontmatter(markdown);

        frontmatter.Should().BeEmpty();
        body.Should().Be(markdown);
    }

    [Fact]
    public void Parse_WithAllRelationTypes_MapsAllRelations()
    {
        var markdown =
            """
            ---
            id: relation-test
            title: Relation Test
            domain: security
            priority: High
            implies: [rule-a, rule-b]
            requires: [rule-c]
            conflictsWith: [rule-d]
            exceptionFor: [rule-e]
            supersedes: [rule-f]
            related: [rule-g, rule-h]
            ---
            Body with all relations.
            """;

        var rule = _parser.Parse(markdown);

        rule.RelatesTo.Should().NotBeNull();
        rule.RelatesTo!.Implies.Should().BeEquivalentTo(new[] { "rule-a", "rule-b" });
        rule.RelatesTo.Requires.Should().BeEquivalentTo(new[] { "rule-c" });
        rule.RelatesTo.ConflictsWith.Should().BeEquivalentTo(new[] { "rule-d" });
        rule.RelatesTo.ExceptionFor.Should().BeEquivalentTo(new[] { "rule-e" });
        rule.RelatesTo.Supersedes.Should().BeEquivalentTo(new[] { "rule-f" });
        rule.RelatesTo.Related.Should().BeEquivalentTo(new[] { "rule-g", "rule-h" });
    }

    [Fact]
    public void Parse_WithOnlyRequiresAndSupersedes_MapsRelations()
    {
        var markdown =
            """
            ---
            id: partial-relation
            title: Partial Relations
            domain: general
            requires: [prereq-1]
            supersedes: [old-rule-1, old-rule-2]
            ---
            Body.
            """;

        var rule = _parser.Parse(markdown);

        rule.RelatesTo.Should().NotBeNull();
        rule.RelatesTo!.Requires.Should().BeEquivalentTo(new[] { "prereq-1" });
        rule.RelatesTo.Supersedes.Should().BeEquivalentTo(new[] { "old-rule-1", "old-rule-2" });
        rule.RelatesTo.Implies.Should().BeEmpty();
        rule.RelatesTo.ConflictsWith.Should().BeEmpty();
    }

    [Fact]
    public void Parse_RelatedMultilineList_MapsRelatedRelation()
    {
        var markdown =
            """
            ---
            id: related-test
            title: Related Test
            domain: docs
            related:
              - doc-a
              - doc-b
            ---
            Body.
            """;

        var rule = _parser.Parse(markdown);

        rule.RelatesTo.Should().NotBeNull();
        rule.RelatesTo!.Related.Should().BeEquivalentTo(new[] { "doc-a", "doc-b" });
    }

    [Fact]
    public void ParseInlineList_StandardList_ReturnsItems()
    {
        var value = "[item1, item2, item3]";

        var result = KnowledgeRuleParser.ParseInlineList(value);

        result.Should().BeEquivalentTo(new[] { "item1", "item2", "item3" });
    }

    // ── F1: validatorScript block-scalar parsing (the load-path regression guard) ──

    private const string ValidatorMarkdown =
        """
        ---
        id: sec-secrets
        title: No Secrets
        domain: security
        type: Constraint
        priority: Critical
        validatorScript: |
          import json, sys
          data = json.load(sys.stdin)
          print(json.dumps({"pass": True, "violations": []}))
        ---
        Rule body here.
        """;

    [Fact]
    public void Parse_ValidatorScriptBlockScalar_ExtractsMultilineScript()
    {
        var rule = _parser.Parse(ValidatorMarkdown);

        rule.ValidatorScript.Should().NotBeNull();
        rule.ValidatorScript.Should().Contain("import json, sys");
        rule.ValidatorScript.Should().Contain("data = json.load(sys.stdin)");
        rule.ValidatorScript!.Should().Contain("\n", because: "the multi-line script must be preserved");
        rule.ValidatorScript.Should().NotContain("---", because: "the block ends at the frontmatter delimiter");
        rule.Body.Should().Be("Rule body here.", because: "the block scalar must not swallow the body");
    }

    [Fact]
    public void Parse_NoValidatorScript_LeavesValidatorScriptNull()
    {
        var rule = _parser.Parse(FullMarkdown);

        rule.ValidatorScript.Should().BeNull();
    }

    // ── F7: validatorEnabled kill-switch flag ──

    [Fact]
    public void Parse_ValidatorEnabledFalse_DisablesValidator()
    {
        var markdown =
            """
            ---
            id: r
            title: R
            domain: security
            validatorEnabled: false
            ---
            Body.
            """;

        _parser.Parse(markdown).ValidatorEnabled.Should().BeFalse();
    }

    [Fact]
    public void Parse_NoValidatorEnabled_DefaultsToTrue()
    {
        _parser.Parse(FullMarkdown).ValidatorEnabled.Should().BeTrue();
    }
}
