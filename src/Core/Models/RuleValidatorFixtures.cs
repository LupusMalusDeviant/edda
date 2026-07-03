namespace Edda.Core.Models;

/// <summary>
/// TDK validator self-test fixtures (F5): code snippets the rule's validator must accept
/// (<see cref="Pass"/>, no violations) or reject (<see cref="Fail"/>, at least one violation).
/// Authoring-time metadata consumed by the fixture verifier; not persisted to the graph and not
/// used during runtime validation.
/// </summary>
public sealed record RuleValidatorFixtures
{
    /// <summary>Snippets that must produce NO violations from the rule's validator.</summary>
    public IReadOnlyList<string> Pass { get; init; } = [];

    /// <summary>Snippets that must produce AT LEAST ONE violation from the rule's validator.</summary>
    public IReadOnlyList<string> Fail { get; init; } = [];
}
