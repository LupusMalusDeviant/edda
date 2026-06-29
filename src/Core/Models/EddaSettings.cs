namespace Edda.Core.Models;

/// <summary>
/// Root container for all persisted, runtime-editable application settings (non-secret).
/// Serialized to JSON by the settings service. Secrets are never part of this model — they live in
/// the credential store. Feature-specific sections (LLM enrichment, embeddings, knowledge sources)
/// are added here as additional properties in later phases.
/// </summary>
public sealed record EddaSettings
{
    /// <summary>
    /// Schema version of the persisted settings document, used for future migrations.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// General, cross-cutting application settings.
    /// </summary>
    public GeneralSettings General { get; init; } = new();

    /// <summary>
    /// Settings for the optional LLM enricher used during knowledge ingestion.
    /// </summary>
    public LlmEnrichmentSettings LlmEnrichment { get; init; } = new();

    /// <summary>
    /// Settings for the embedding model used for both DB indexing and query embedding.
    /// </summary>
    public EmbeddingSettings Embedding { get; init; } = new();

    /// <summary>
    /// Configured knowledge-source instances (Git repositories and other connectors). Holds non-secret
    /// field values only; per-instance secrets live in the credential store.
    /// </summary>
    public IReadOnlyList<ConnectorInstanceConfig> Sources { get; init; } = [];

    /// <summary>
    /// MCP-server exposure settings (which tools external LLMs may use; read-only by default).
    /// </summary>
    public McpSettings Mcp { get; init; } = new();

    /// <summary>
    /// Adaptive document-chunking settings (see ADR-0008). Controls how large documents are split into
    /// embedded chunks for retrieval; the document itself stays a single graph node.
    /// </summary>
    public ChunkingSettings Chunking { get; init; } = new();
}

/// <summary>
/// Settings for the embedding model. A single model serves both the stored DB embeddings and the
/// query/agent-prompt embeddings, because the two are only comparable within the same vector space. The
/// API key is never stored here — it lives in the credential store under <c>{userId}:embed:{provider}</c>.
/// Each value is nullable; a null value falls back to the corresponding <c>EMBEDDING_*</c> environment
/// variable or <c>Embeddings:*</c> configuration. Changing the provider, model or dimensions invalidates
/// the stored DB embeddings and requires a re-embed.
/// </summary>
public sealed record EmbeddingSettings
{
    /// <summary>Provider key (openai | google | voyage | ollama | custom | null). Null falls back to <c>EMBEDDING_PROVIDER</c>.</summary>
    public string? Provider { get; init; }

    /// <summary>Model identifier; null falls back to <c>EMBEDDING_MODEL</c> or the provider default.</summary>
    public string? Model { get; init; }

    /// <summary>API base URL (Ollama/Custom); null falls back to <c>EMBEDDING_BASE_URL</c> or the provider default.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Vector dimensions (Ollama/Custom); null falls back to <c>EMBEDDING_DIMENSIONS</c> or the provider default.</summary>
    public int? Dimensions { get; init; }

    /// <summary>AWS region for the Bedrock provider (e.g. <c>us-east-1</c>); null falls back to <c>EMBEDDING_REGION</c>.</summary>
    public string? Region { get; init; }
}

/// <summary>
/// Settings for adaptive document chunking (see ADR-0008). Large documents are split into multiple
/// embedded chunks for semantic retrieval, while the document remains a single graph node (the chunks
/// are never surfaced as graph nodes). Each value is nullable; a null value falls back to the corresponding
/// <c>CHUNKING_*</c> environment variable or the built-in default. Changing these invalidates stored chunk
/// embeddings and requires a re-embed.
/// </summary>
public sealed record ChunkingSettings
{
    /// <summary>Whether chunking is active. Null falls back to <c>CHUNKING_ENABLED</c> (default true).</summary>
    public bool? Enabled { get; init; }

    /// <summary>Maximum chunk size in characters. Null falls back to <c>CHUNKING_MAX_CHARS</c> or the default.</summary>
    public int? MaxChars { get; init; }

    /// <summary>
    /// Overlap between adjacent text chunks in characters. Null falls back to <c>CHUNKING_OVERLAP_CHARS</c>
    /// or the default.
    /// </summary>
    public int? OverlapChars { get; init; }
}

/// <summary>
/// Settings for the optional LLM enricher (see ADR-0001). The API key/token is never stored here — it
/// lives in the credential store under <c>{userId}:llm:{provider}</c>. Each value is nullable; a null
/// value falls back to the corresponding <c>INGESTION_*</c> environment variable.
/// </summary>
public sealed record LlmEnrichmentSettings
{
    /// <summary>
    /// Whether LLM enrichment is active. Null falls back to the <c>INGESTION_ENRICHER</c> environment
    /// variable (enabled when it equals <c>llm</c>).
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Provider key (anthropic | openai | ollama | bedrock | openrouter | gemini | custom). Null falls
    /// back to <c>INGESTION_LLM_PROVIDER</c>.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>Model identifier; null falls back to <c>INGESTION_LLM_MODEL</c> or the provider default.</summary>
    public string? Model { get; init; }

    /// <summary>API base URL; null falls back to <c>INGESTION_LLM_BASE_URL</c> or the provider default.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>AWS region for the Bedrock provider; ignored by other providers.</summary>
    public string? Region { get; init; }
}

/// <summary>
/// General, cross-cutting application settings. Each value is nullable; a null value means
/// "not overridden in the UI", and the consuming code falls back to its environment-variable default.
/// </summary>
public sealed record GeneralSettings
{
    /// <summary>
    /// Whether the knowledge ingestion endpoint is enabled. A null value falls back to the
    /// <c>ENABLE_INGESTION</c> environment variable.
    /// </summary>
    public bool? EnableIngestion { get; init; }
}

/// <summary>
/// Settings controlling what the MCP server exposes to external clients/LLMs. Each value is nullable; a
/// null value falls back to the corresponding <c>MCP_*</c> environment variable. External MCP access is
/// read-only by default — mutating tools are never exposed unless <see cref="AllowWriteTools"/> is true.
/// </summary>
public sealed record McpSettings
{
    /// <summary>
    /// Whether MCP exposes any tools. False exposes nothing (live off); null exposes the configured/default
    /// tools when the <c>/mcp</c> endpoint is enabled (<c>MCP_SERVER_ENABLED</c>).
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Allow-listed tool names exposed via MCP. Null/empty falls back to <c>MCP_EXPOSED_TOOLS</c> or the
    /// read-only default tools.
    /// </summary>
    public IReadOnlyList<string>? ExposedTools { get; init; }

    /// <summary>
    /// Whether mutating tools may be exposed via MCP. Null falls back to <c>MCP_ALLOW_WRITE_TOOLS</c>
    /// (default false → read-only).
    /// </summary>
    public bool? AllowWriteTools { get; init; }
}
