using Edda.Core.Benchmark;

namespace Edda.Core.Tests.Benchmark;

/// <summary>Unit tests for <see cref="SyntheticBenchmarkGenerator"/> (pure, deterministic, no infrastructure).</summary>
public class SyntheticBenchmarkGeneratorTests
{
    private readonly SyntheticBenchmarkGenerator _sut = new();

    [Fact]
    public void Generate_SameArguments_IsDeterministic()
    {
        var a = _sut.Generate(200, 10, seed: 42);
        var b = _sut.Generate(200, 10, seed: 42);

        a.Rules.Select(r => r.Id).Should().Equal(b.Rules.Select(r => r.Id));
        a.Rules.Select(r => r.Body).Should().Equal(b.Rules.Select(r => r.Body));
        a.Dataset.Cases.Select(c => c.Id).Should().Equal(b.Dataset.Cases.Select(c => c.Id));
        a.Dataset.Cases.Select(c => string.Join(",", c.ExpectedRuleIds))
            .Should().Equal(b.Dataset.Cases.Select(c => string.Join(",", c.ExpectedRuleIds)));
    }

    [Fact]
    public void Generate_ProducesRequestedRuleCount()
        => _sut.Generate(500, 10).Rules.Should().HaveCount(500);

    [Fact]
    public void Generate_ExpectedRulesCarryTheCaseTopic()
    {
        var corpus = _sut.Generate(300, 15, seed: 5);
        var byId = corpus.Rules.ToDictionary(r => r.Id);

        corpus.Dataset.Cases.Should().NotBeEmpty();
        foreach (var c in corpus.Dataset.Cases)
        {
            var topicToken = c.Concepts.Should().ContainSingle().Subject;
            c.Query.Should().Contain(topicToken);
            c.ExpectedRuleIds.Should().NotBeEmpty();
            foreach (var ruleId in c.ExpectedRuleIds)
            {
                byId[ruleId].Tags.Should().Contain(topicToken);
                byId[ruleId].WhenRelevant!.DetectedConcepts.Should().Contain(topicToken);
            }
        }
    }

    [Fact]
    public void Generate_CaseCount_ClampedToDistinctTopics()
    {
        // ~ruleCount / 5 topics exist, so an over-large caseCount cannot exceed that.
        var corpus = _sut.Generate(50, 1000);

        corpus.Dataset.Cases.Count.Should().BeLessThanOrEqualTo(50 / 5);
    }

    [Fact]
    public void Generate_DifferentSeed_ChangesAssignment()
    {
        var a = _sut.Generate(200, 10, seed: 1);
        var b = _sut.Generate(200, 10, seed: 2);

        a.Rules.Select(r => r.Tags[1]).Should().NotEqual(b.Rules.Select(r => r.Tags[1]));
    }

    [Fact]
    public void Generate_ZeroRules_YieldsEmptyCorpus()
    {
        var corpus = _sut.Generate(0, 5);

        corpus.Rules.Should().BeEmpty();
        corpus.Dataset.Cases.Should().BeEmpty();
    }
}
