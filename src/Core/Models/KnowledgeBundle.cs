using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edda.Core.Models;

/// <summary>
/// A portable, lossless export of knowledge rules (including their typed relations) for transfer between
/// Edda instances or from another tool. Embeddings are intentionally omitted: they are model-specific and
/// recomputed locally on import, so importing foreign vectors would be meaningless (see ADR-0007).
/// </summary>
public sealed record KnowledgeBundle
{
    /// <summary>Schema version of the bundle format.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>The exported rules, including their relations.</summary>
    public IReadOnlyList<KnowledgeRule> Rules { get; init; } = [];
}

/// <summary>
/// Shared JSON options for reading and writing a <see cref="KnowledgeBundle"/>. Used by both the export
/// endpoint and the importer so a bundle round-trips exactly. Enums are written as readable strings.
/// </summary>
public static class KnowledgeBundleSerialization
{
    /// <summary>The canonical serializer options for knowledge bundles.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
