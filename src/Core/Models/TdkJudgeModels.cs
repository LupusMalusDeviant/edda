namespace Edda.Core.Models;

/// <summary>A single LLM-judge evaluation request (F16): one rule prompt against one code block.</summary>
public sealed record TdkJudgeRequest
{
    /// <summary>The rule whose prompt is evaluated.</summary>
    public required string RuleId { get; init; }

    /// <summary>The rule's judge prompt (frontmatter <c>validatorPrompt</c>).</summary>
    public required string Prompt { get; init; }

    /// <summary>The code block under evaluation.</summary>
    public required string Code { get; init; }

    /// <summary>The code block's language identifier (may be empty).</summary>
    public string Language { get; init; } = "";
}

/// <summary>The judge's verdict for one request (F16).</summary>
public sealed record TdkJudgeResult
{
    /// <summary>True when the judge actually produced a parseable verdict. False = engine error.</summary>
    public required bool Executed { get; init; }

    /// <summary>The pass verdict (only meaningful when <see cref="Executed"/>).</summary>
    public bool Pass { get; init; }

    /// <summary>Violations reported by the judge (rule id already filled in).</summary>
    public IReadOnlyList<TdkViolation> Violations { get; init; } = [];

    /// <summary>Short error description when <see cref="Executed"/> is false.</summary>
    public string? Error { get; init; }
}
