using Edda.AKG.Parser;

namespace Edda.AKG.Tests.Parser;

/// <summary>F5: parsing of the nested <c>validatorFixtures</c> frontmatter block.</summary>
public class KnowledgeRuleParserFixturesTests
{
    private readonly KnowledgeRuleParser _parser = new();

    // Builds a rule Markdown with an arbitrary frontmatter fixtures block (explicit newlines keep the
    // YAML indentation unambiguous).
    private static string Markdown(string fixturesBlock) =>
        "---\n" +
        "id: fx-rule\n" +
        "title: FX Rule\n" +
        "domain: coding\n" +
        "priority: High\n" +
        fixturesBlock +
        "---\n" +
        "Body.";

    [Fact]
    public void Parse_ValidatorFixtures_ParsesPassAndFail()
    {
        var markdown = Markdown(
            "validatorFixtures:\n" +
            "  pass:\n" +
            "    - |\n" +
            "      ok_code = 1\n" +
            "  fail:\n" +
            "    - |\n" +
            "      bad_code = 2\n");

        var rule = _parser.Parse(markdown);

        rule.ValidatorFixtures.Should().NotBeNull();
        rule.ValidatorFixtures!.Pass.Should().ContainSingle().Which.Should().Be("ok_code = 1");
        rule.ValidatorFixtures.Fail.Should().ContainSingle().Which.Should().Be("bad_code = 2");
    }

    [Fact]
    public void Parse_ValidatorFixtures_MultipleSnippetsPerList()
    {
        var markdown = Markdown(
            "validatorFixtures:\n" +
            "  pass:\n" +
            "    - |\n" +
            "      first_ok = 1\n" +
            "    - |\n" +
            "      second_ok = 2\n");

        var rule = _parser.Parse(markdown);

        rule.ValidatorFixtures!.Pass.Should().Equal("first_ok = 1", "second_ok = 2");
        rule.ValidatorFixtures.Fail.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ValidatorFixtures_OnlyFail_PassEmpty()
    {
        var markdown = Markdown(
            "validatorFixtures:\n" +
            "  fail:\n" +
            "    - |\n" +
            "      boom = 1\n");

        var rule = _parser.Parse(markdown);

        rule.ValidatorFixtures!.Pass.Should().BeEmpty();
        rule.ValidatorFixtures.Fail.Should().ContainSingle().Which.Should().Be("boom = 1");
    }

    [Fact]
    public void Parse_ValidatorFixtures_PreservesMultilineCode()
    {
        var markdown = Markdown(
            "validatorFixtures:\n" +
            "  pass:\n" +
            "    - |\n" +
            "      try:\n" +
            "          value = int(raw)\n" +
            "      except ValueError:\n" +
            "          value = 0\n");

        var rule = _parser.Parse(markdown);

        rule.ValidatorFixtures!.Pass.Should().ContainSingle();
        rule.ValidatorFixtures.Pass[0].Should().Be(
            "try:\n    value = int(raw)\nexcept ValueError:\n    value = 0");
    }

    [Fact]
    public void Parse_NoValidatorFixtures_LeavesNull()
    {
        var markdown = Markdown(fixturesBlock: "");

        var rule = _parser.Parse(markdown);

        rule.ValidatorFixtures.Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyValidatorFixtures_LeavesNull()
    {
        // A validatorFixtures key with no pass/fail items yields no fixtures.
        var markdown = Markdown(
            "validatorFixtures:\n" +
            "  pass:\n");

        var rule = _parser.Parse(markdown);

        rule.ValidatorFixtures.Should().BeNull();
    }
}
