namespace Edda.Core.Models;

/// <summary>Aggregated result of verifying many rules' validator fixtures (F5).</summary>
public sealed record TdkFixtureVerificationReport
{
    /// <summary>Per-rule reports (only rules that declared fixtures).</summary>
    public IReadOnlyList<TdkFixtureRuleReport> Rules { get; init; } = [];

    /// <summary>Number of rules whose fixtures all passed.</summary>
    public int VerifiedCount => Rules.Count(r => r.Verified);

    /// <summary>Number of rules with fixtures examined.</summary>
    public int TotalCount => Rules.Count;
}

/// <summary>Fixture-verification result for a single rule.</summary>
public sealed record TdkFixtureRuleReport
{
    /// <summary>The rule's ID.</summary>
    public required string RuleId { get; init; }

    /// <summary>True when the rule declares a validator and at least one fixture.</summary>
    public bool HasFixtures { get; init; }

    /// <summary>True when every fixture case matched its expectation.</summary>
    public bool Verified { get; init; }

    /// <summary>Per-fixture case results.</summary>
    public IReadOnlyList<TdkFixtureCaseResult> Cases { get; init; } = [];
}

/// <summary>Result of one fixture snippet run.</summary>
public sealed record TdkFixtureCaseResult
{
    /// <summary>"pass" or "fail" — which fixture list the snippet came from.</summary>
    public required string Kind { get; init; }

    /// <summary>Zero-based index within its fixture list.</summary>
    public required int Index { get; init; }

    /// <summary>True when the validator's verdict matched the expectation.</summary>
    public required bool Ok { get; init; }

    /// <summary>True when the validator could not run (sandbox absent, crash, timeout, bad JSON).</summary>
    public bool EngineError { get; init; }

    /// <summary>Short human-readable explanation when <see cref="Ok"/> is false.</summary>
    public string? Detail { get; init; }
}
