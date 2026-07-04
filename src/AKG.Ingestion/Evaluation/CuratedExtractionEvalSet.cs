using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Evaluation;

/// <summary>
/// A small, hand-curated golden dataset for entity-extraction evaluation. Each case pairs a short text with the
/// entities and relations a competent extractor should surface. Intended to be run against a real LLM-backed
/// extractor (e.g. Ollama) to measure extraction quality — see <c>docs/benchmarks/extraktions-eval.md</c>.
/// </summary>
public static class CuratedExtractionEvalSet
{
    /// <summary>The default curated golden dataset (v1).</summary>
    public static ExtractionEvalDataset Default { get; } = new()
    {
        Name = "curated-extraction-v1",
        Cases =
        [
            new ExtractionEvalCase
            {
                Id = "neo4j-cypher",
                Text = "Neo4j is a graph database. It is queried with the Cypher query language.",
                DomainHint = "technology",
                Expected = new EntityExtractionResult
                {
                    Entities =
                    [
                        new ExtractedEntity { Name = "Neo4j", Type = "technology" },
                        new ExtractedEntity { Name = "Cypher", Type = "technology" },
                    ],
                    Relations =
                    [
                        new ExtractedRelation { Source = "Neo4j", Target = "Cypher", Description = "queried with" },
                    ],
                },
            },
            new ExtractionEvalCase
            {
                Id = "acme-berlin",
                Text = "Acme Corp is headquartered in Berlin. Its CEO is Jane Doe.",
                Expected = new EntityExtractionResult
                {
                    Entities =
                    [
                        new ExtractedEntity { Name = "Acme Corp", Type = "organization" },
                        new ExtractedEntity { Name = "Berlin", Type = "location" },
                        new ExtractedEntity { Name = "Jane Doe", Type = "person" },
                    ],
                    Relations =
                    [
                        new ExtractedRelation { Source = "Acme Corp", Target = "Berlin", Description = "headquartered in" },
                        new ExtractedRelation { Source = "Jane Doe", Target = "Acme Corp", Description = "CEO of" },
                    ],
                },
            },
            new ExtractionEvalCase
            {
                Id = "photosynthesis",
                Text = "Photosynthesis converts sunlight into chemical energy in plants, producing oxygen.",
                DomainHint = "biology",
                Expected = new EntityExtractionResult
                {
                    Entities =
                    [
                        new ExtractedEntity { Name = "Photosynthesis", Type = "concept" },
                        new ExtractedEntity { Name = "sunlight", Type = "concept" },
                        new ExtractedEntity { Name = "oxygen", Type = "concept" },
                    ],
                    Relations =
                    [
                        new ExtractedRelation { Source = "Photosynthesis", Target = "oxygen", Description = "produces" },
                    ],
                },
            },
        ],
    };
}
