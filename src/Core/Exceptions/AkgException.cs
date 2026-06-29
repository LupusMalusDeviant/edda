namespace Edda.Core.Exceptions;

/// <summary>
/// Base exception for Agent Knowledge Graph errors.
/// </summary>
public class AkgException : EddaException
{
    /// <summary>Initializes a new AkgException.</summary>
    public AkgException(string message, string? correlationId = null, Exception? innerException = null)
        : base("AKG", message, correlationId, innerException) { }
}

/// <summary>
/// Thrown when a knowledge rule file contains invalid or missing frontmatter fields.
/// </summary>
public sealed class RuleParseException : AkgException
{
    /// <summary>Path to the file that failed to parse.</summary>
    public string FilePath { get; }

    /// <summary>Name of the required field that was missing or invalid.</summary>
    public string MissingField { get; }

    /// <summary>Initializes a new RuleParseException.</summary>
    public RuleParseException(string filePath, string missingField)
        : base($"Required field '{missingField}' missing in {filePath}")
    {
        FilePath = filePath;
        MissingField = missingField;
    }
}

/// <summary>
/// Thrown when a cycle is detected in the IMPLIES relationship graph.
/// </summary>
public sealed class CyclicDependencyException : AkgException
{
    /// <summary>The rule IDs that form the cycle.</summary>
    public IReadOnlyList<string> Cycle { get; }

    /// <summary>Initializes a new CyclicDependencyException.</summary>
    public CyclicDependencyException(IReadOnlyList<string> cycle)
        : base($"Cyclic dependency detected: {string.Join(" → ", cycle)}")
    {
        Cycle = cycle;
    }
}

/// <summary>
/// Thrown when a rule references a domain that does not exist in the graph.
/// </summary>
public sealed class DomainNotFoundException : AkgException
{
    /// <summary>The domain name that was not found.</summary>
    public string DomainName { get; }

    /// <summary>Initializes a new DomainNotFoundException.</summary>
    public DomainNotFoundException(string domainName)
        : base($"Domain '{domainName}' not found in the knowledge graph.")
    {
        DomainName = domainName;
    }
}
