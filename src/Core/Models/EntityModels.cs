namespace Edda.Core.Models;

/// <summary>An entity extracted from text (LightRAG-style): a named thing with a type and description.</summary>
public sealed record ExtractedEntity
{
    /// <summary>Display name of the entity (e.g. "Neo4j", "Erico Lenk").</summary>
    public required string Name { get; init; }

    /// <summary>Coarse category (e.g. "technology", "person", "concept"). Defaults to "concept".</summary>
    public string Type { get; init; } = "concept";

    /// <summary>Short description of the entity as understood from the source text.</summary>
    public string Description { get; init; } = "";
}

/// <summary>A directed relationship between two extracted entities.</summary>
public sealed record ExtractedRelation
{
    /// <summary>Name of the source entity (matches an <see cref="ExtractedEntity.Name"/>).</summary>
    public required string Source { get; init; }

    /// <summary>Name of the target entity (matches an <see cref="ExtractedEntity.Name"/>).</summary>
    public required string Target { get; init; }

    /// <summary>Description of how the two entities relate.</summary>
    public string Description { get; init; } = "";

    /// <summary>Optional keywords characterizing the relationship.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

/// <summary>The result of entity/relation extraction over a single text.</summary>
public sealed record EntityExtractionResult
{
    /// <summary>Extracted entities.</summary>
    public IReadOnlyList<ExtractedEntity> Entities { get; init; } = [];

    /// <summary>Extracted relations between entities.</summary>
    public IReadOnlyList<ExtractedRelation> Relations { get; init; } = [];

    /// <summary>A shared empty result (no entities, no relations).</summary>
    public static EntityExtractionResult Empty { get; } = new();
}

/// <summary>An entity as stored in / retrieved from the graph entity layer.</summary>
public sealed record GraphEntity
{
    /// <summary>Display name of the entity.</summary>
    public required string Name { get; init; }

    /// <summary>Coarse category of the entity.</summary>
    public string Type { get; init; } = "concept";

    /// <summary>Stored description.</summary>
    public string Description { get; init; } = "";

    /// <summary>How many times the entity has been ingested (mention count).</summary>
    public int Mentions { get; init; }
}

/// <summary>Counts produced by an entity ingestion run.</summary>
public sealed record EntityIngestionResult
{
    /// <summary>Number of entities created or updated.</summary>
    public int EntitiesIngested { get; init; }

    /// <summary>Number of relations created or updated.</summary>
    public int RelationsIngested { get; init; }

    /// <summary>A shared empty result (nothing ingested).</summary>
    public static EntityIngestionResult Empty { get; } = new();
}

/// <summary>Request body for the entity-ingestion endpoint.</summary>
public sealed record EntityIngestRequest
{
    /// <summary>The text to extract entities and relations from.</summary>
    public required string Text { get; init; }

    /// <summary>Optional domain hint passed to the extractor.</summary>
    public string? DomainHint { get; init; }
}
