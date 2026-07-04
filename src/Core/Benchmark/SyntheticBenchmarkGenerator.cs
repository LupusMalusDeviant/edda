using Edda.Core.Models;

namespace Edda.Core.Benchmark;

/// <summary>A generated synthetic rule corpus together with a matching benchmark dataset.</summary>
/// <param name="Rules">The synthetic rules to load into a knowledge graph before running the benchmark.</param>
/// <param name="Dataset">Benchmark cases whose expected rule ids are known by construction.</param>
public sealed record SyntheticCorpus(IReadOnlyList<KnowledgeRule> Rules, BenchmarkDataset Dataset);

/// <summary>
/// Deterministic generator of synthetic AKG rules and matching benchmark cases for scale/latency testing.
/// Given a size and seed it produces a fixed rule corpus plus a query dataset whose ground truth is known by
/// construction: each rule is tagged with one "topic" token, and each case queries a single topic whose
/// expected rules are exactly the rules carrying that token. The output is a pure function of
/// (ruleCount, caseCount, seed) — no randomness (no <see cref="System.Random"/>), no infrastructure, no clock —
/// so a run is fully reproducible. Embeddings are intentionally absent: the corpus exercises the keyword +
/// graph pipeline, which is the path that scales with corpus size.
/// </summary>
public sealed class SyntheticBenchmarkGenerator
{
    /// <summary>Approximate number of rules that share each topic token (controls ground-truth set size).</summary>
    private const int RulesPerTopic = 5;

    /// <summary>Number of distinct domains rules are spread across.</summary>
    private const int DomainCount = 24;

    /// <summary>Upper bound on a case's expected-rule set, keeping metrics well-defined at any scale.</summary>
    private const int MaxExpectedPerCase = 20;

    private static readonly RulePriority[] Priorities = [RulePriority.Low, RulePriority.Medium, RulePriority.High];

    /// <summary>
    /// Generates <paramref name="ruleCount"/> synthetic rules and up to <paramref name="caseCount"/> benchmark
    /// cases. Deterministic in all three arguments.
    /// </summary>
    /// <param name="ruleCount">Number of rules to generate (clamped to at least 0).</param>
    /// <param name="caseCount">Number of benchmark cases to generate (clamped to the number of distinct topics).</param>
    /// <param name="seed">Seed mixed into the deterministic topic/domain assignment.</param>
    /// <returns>The rule corpus and its benchmark dataset.</returns>
    public SyntheticCorpus Generate(int ruleCount, int caseCount, int seed = 1)
    {
        ruleCount = Math.Max(0, ruleCount);
        var topicCount = Math.Max(1, ruleCount / RulesPerTopic);

        var rules = new List<KnowledgeRule>(ruleCount);
        // Preserve first-seen topic order so case selection is deterministic and independent of dictionary internals.
        var topicToRuleIds = new Dictionary<int, List<string>>();
        var topicOrder = new List<int>();

        for (var i = 0; i < ruleCount; i++)
        {
            var topic = (int)(Hash(i, seed + 7) % (uint)topicCount);
            var topicToken = $"topic-{topic}";
            var domain = $"dom-{(int)(Hash(i, seed + 3) % DomainCount)}";
            var id = $"bench-{i}";

            rules.Add(new KnowledgeRule
            {
                Id = id,
                Type = "Rule",
                Domain = domain,
                Priority = Priorities[i % Priorities.Length],
                Tags = [$"tag-{i % 12}", topicToken],
                Body =
                    $"Synthetic benchmark rule {i} about {topicToken}. Applies in domain {domain}. " +
                    $"Filler concept c{i % 97} and c{i % 89} for keyword scoring.",
                WhenRelevant = new WhenRelevant { DetectedConcepts = [topicToken] },
            });

            if (!topicToRuleIds.TryGetValue(topic, out var bucket))
            {
                bucket = [];
                topicToRuleIds[topic] = bucket;
                topicOrder.Add(topic);
            }

            bucket.Add(id);
        }

        var cases = new List<BenchmarkCase>();
        var targetCases = Math.Max(0, Math.Min(caseCount, topicOrder.Count));
        for (var j = 0; j < targetCases; j++)
        {
            // Spread the chosen topics across the corpus via a stride co-prime-ish to the topic count.
            var topic = topicOrder[(int)(Hash(j, seed + 11) % (uint)topicOrder.Count)];
            var topicToken = $"topic-{topic}";
            var expected = topicToRuleIds[topic];

            cases.Add(new BenchmarkCase
            {
                Id = $"case-{j}",
                Query = $"Guidance and rules on {topicToken} for this task.",
                Concepts = [topicToken],
                ExpectedRuleIds = expected.Count <= MaxExpectedPerCase
                    ? expected
                    : expected.Take(MaxExpectedPerCase).ToList(),
            });
        }

        return new SyntheticCorpus(
            rules,
            new BenchmarkDataset { Name = $"synthetic-{ruleCount}-seed{seed}", Cases = cases });
    }

    /// <summary>Deterministic 32-bit mix of an index and a salt (Knuth multiplicative hash). No RNG.</summary>
    private static uint Hash(int index, int salt)
    {
        unchecked
        {
            var x = (uint)index * 2654435761u + (uint)salt * 40503u + 2166136261u;
            x ^= x >> 15;
            x *= 2246822519u;
            x ^= x >> 13;
            return x;
        }
    }
}
