using System.Security.Cryptography;
using System.Text;
using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Edda.Core.Models;
using Edda.Core.Resilience;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Embeddings;

/// <summary>
/// Manages embedding caching in Neo4j. Each rule body is split by an <see cref="IDocumentChunker"/> into
/// one or more chunks; the chunks are embedded and stored as hidden <c>(:RuleChunk)</c> children of the
/// rule (<c>(r:Rule)-[:HAS_CHUNK]->(:RuleChunk)</c>), never as graph nodes themselves. A SHA-256 hash of the
/// rule body is kept on the rule so only rules whose body changed are re-chunked and re-embedded (see
/// ADR-0008). Small bodies (or chunking disabled) yield a single chunk, matching the previous
/// one-embedding-per-document behaviour.
/// </summary>
internal sealed class Neo4jEmbeddingCache : INeo4jEmbeddingCache
{
    private readonly ICypherExecutor _cypher;
    private readonly IEmbeddingService _embeddings;
    private readonly IDocumentChunker _chunker;
    private readonly Func<ChunkingOptions> _chunkingOptions;
    private readonly ILogger<Neo4jEmbeddingCache> _logger;
    private readonly IActivityTracker? _activity;
    private readonly int _maxParallelism;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Max embedding attempts per rule before the backfill treats it as failed and stops retrying it.
    /// A few permanently-failing rules can therefore never block the rest of the corpus from embedding.
    /// This is the coarse, cross-cycle cap (persisted as <c>embedAttempts</c>); it complements the fast
    /// in-call retries governed by <see cref="MaxTransientRetries"/>.
    /// </summary>
    private const int MaxEmbedAttempts = 5;

    /// <summary>
    /// Number of immediate in-call retries for a <em>transient</em> provider failure (rate limit, 5xx,
    /// network blip) before the attempt is abandoned and recorded via <see cref="MaxEmbedAttempts"/>.
    /// A short exponential backoff (<see cref="ExponentialBackoff"/>) separates the retries so a brief
    /// provider hiccup no longer burns one of the scarce cross-cycle attempts.
    /// </summary>
    private const int MaxTransientRetries = 3;

    /// <summary>Delay before the first transient retry; subsequent retries double it up to <see cref="MaxRetryDelay"/>.</summary>
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound on a single transient-retry backoff delay.</summary>
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Maximum jitter added to a backoff delay (fraction of the delay) to avoid a thundering herd.</summary>
    private const double RetryJitterFraction = 0.2;

    /// <summary>
    /// Serializes rebuilds: the background backfill and a manual/post-import rebuild must never run
    /// concurrently (they would compete over the same rules). A second caller skips while one is active.
    /// </summary>
    private readonly SemaphoreSlim _rebuildGate = new(1, 1);

    // ── Live embedding progress tracking ────────────────────────────────────
    private volatile int _totalToEmbed;
    // Not volatile: written via Interlocked (which provides the barrier) under parallel rebuild.
    private int _embeddedSoFar;
    private volatile bool _isRebuilding;
    private volatile string? _currentRuleId;
    private CancellationTokenSource? _rebuildCts;

    /// <summary>Total number of rules that need embedding in the current rebuild cycle.</summary>
    public int TotalToEmbed => _totalToEmbed;

    /// <summary>Number of rules already embedded in the current rebuild cycle.</summary>
    public int EmbeddedSoFar => Volatile.Read(ref _embeddedSoFar);

    /// <summary>True while a rebuild is in progress.</summary>
    public bool IsRebuilding => _isRebuilding;

    /// <summary>The rule ID currently being embedded, or null if idle.</summary>
    public string? CurrentRuleId => _currentRuleId;

    /// <summary>
    /// Initializes a new instance of <see cref="Neo4jEmbeddingCache"/>.
    /// </summary>
    /// <param name="cypher">Cypher executor for graph read/write operations.</param>
    /// <param name="embeddings">Embedding service for generating vectors.</param>
    /// <param name="chunker">Splits rule bodies into embeddable chunks.</param>
    /// <param name="chunkingOptions">Resolves the current chunking options at call time (live settings).</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="activity">Optional tracker for surfacing rebuild progress in the global UI indicator.</param>
    /// <param name="maxParallelism">
    /// Maximum number of rules embedded concurrently during a rebuild. Each provider call and Neo4j write
    /// uses its own session/HTTP request, so concurrency is safe; values &gt;1 mainly speed up
    /// network-bound (cloud) providers. Clamped to at least 1.
    /// </param>
    /// <param name="timeProvider">
    /// Clock abstraction used to delay between transient-failure retries. Defaults to
    /// <see cref="TimeProvider.System"/>; tests inject a fake to make the backoff deterministic.
    /// </param>
    public Neo4jEmbeddingCache(
        ICypherExecutor cypher,
        IEmbeddingService embeddings,
        IDocumentChunker chunker,
        Func<ChunkingOptions> chunkingOptions,
        ILogger<Neo4jEmbeddingCache> logger,
        IActivityTracker? activity = null,
        int maxParallelism = 4,
        TimeProvider? timeProvider = null)
    {
        _cypher = cypher;
        _embeddings = embeddings;
        _chunker = chunker;
        _chunkingOptions = chunkingOptions;
        _logger = logger;
        _activity = activity;
        _maxParallelism = Math.Max(1, maxParallelism);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Rebuilds chunk embeddings for all rules that have no chunks yet or no body hash.
    /// No-op if <see cref="IEmbeddingService.IsAvailable"/> is <see langword="false"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RebuildAsync(CancellationToken ct)
    {
        if (!_embeddings.IsAvailable)
        {
            _logger.LogInformation("Embedding service unavailable; skipping cache rebuild | {Component}", "AKG");
            return;
        }

        // Only one rebuild at a time: the background backfill and a manual/post-import rebuild must not overlap.
        if (!await _rebuildGate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            _logger.LogDebug("Embedding rebuild already in progress; skipping concurrent run | {Component}", "AKG");
            return;
        }

        // A dedicated, cancellable token source so the UI can abort a long rebuild (links to the caller's ct).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _rebuildCts = cts;
        var token = cts.Token;

        try
        {
            // Ensure the native vector index exists (best-effort; no-op on providers without support).
            await EnsureVectorIndexAsync(token).ConfigureAwait(false);

            // Find rules without current chunks (missing chunks or no body hash) that have not yet hit the
            // retry cap — so a handful of permanently-failing rules can never block the rest of the backfill.
            var rows = await _cypher.QueryAsync(
                """
                MATCH (r:Rule)
                OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk)
                WITH r, count(c) AS chunks
                WHERE (r.bodyHash IS NULL OR chunks = 0) AND coalesce(r.embedAttempts, 0) < $maxAttempts
                RETURN r.id AS id, r.body AS body, r.chunkStyle AS chunkStyle
                """,
                new { maxAttempts = MaxEmbedAttempts },
                token).ConfigureAwait(false);

            var toEmbed = rows
                .Where(r => r.ContainsKey("id") && r["id"] != null)
                .Select(r => (
                    Id: r["id"]!.ToString()!,
                    Body: r.TryGetValue("body", out var b) ? b?.ToString() ?? string.Empty : string.Empty,
                    ChunkStyle: r.TryGetValue("chunkStyle", out var cs) ? cs?.ToString() : null))
                .Where(x => !string.IsNullOrEmpty(x.Id))
                .ToList();

            if (toEmbed.Count == 0)
            {
                _logger.LogDebug("All embeddings up to date | {Component}", "AKG");
                return;
            }

            _totalToEmbed = toEmbed.Count;
            _embeddedSoFar = 0;
            _isRebuilding = true;

            // Surface the rebuild in the global progress indicator. Chunking is an inseparable part of the
            // embedding rebuild (each rule body is chunked, then embedded), so both are reported together.
            _activity?.Report(ActivityKind.Chunking, ActivityState.Running, onCancel: CancelRebuild);
            _activity?.Report(ActivityKind.Embedding, ActivityState.Running, $"0/{toEmbed.Count}", onCancel: CancelRebuild);

            _logger.LogInformation(
                "Starting embedding rebuild for {Count} rules | {Component}", toEmbed.Count, "AKG");

            var cancelled = false;
            var failures = 0;
            // Throttle live progress reports to the activity indicator to at most ~50 updates per rebuild.
            var progressStep = Math.Max(1, toEmbed.Count / 50);
            var degreeOfParallelism = Math.Min(_maxParallelism, toEmbed.Count);
            try
            {
                // Embed rules concurrently. Each EmbedRuleAsync opens its own Neo4j session and provider request
                // (both safe for concurrent use), and rules are distinct nodes — so there is no contention.
                // A single rule that fails is recorded (embedAttempts++) and skipped; it never aborts the run.
                await Parallel.ForEachAsync(
                    toEmbed,
                    new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = token },
                    async (rule, itemCt) =>
                    {
                        _currentRuleId = rule.Id;
                        try
                        {
                            await EmbedRuleAsync(rule.Id, rule.Body, rule.ChunkStyle, itemCt).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Interlocked.Increment(ref failures);
                            _logger.LogWarning(ex,
                                "Embedding failed for rule '{RuleId}' — recording attempt and continuing | {Component}",
                                rule.Id, "AKG");
                            await RecordEmbedFailureAsync(rule.Id).ConfigureAwait(false);
                            return;
                        }

                        var done = Interlocked.Increment(ref _embeddedSoFar);
                        if (done % progressStep == 0 || done == toEmbed.Count)
                            _activity?.Report(ActivityKind.Embedding, ActivityState.Running,
                                $"{done}/{toEmbed.Count}", onCancel: CancelRebuild);
                    }).ConfigureAwait(false);

                _logger.LogInformation(
                    "Embedding rebuild finished: {Done} embedded, {Failed} failed ({Dop}-way parallel) | {Component}",
                    Volatile.Read(ref _embeddedSoFar), failures, degreeOfParallelism, "AKG");
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                _logger.LogInformation(
                    "Embedding rebuild cancelled after {Done}/{Total} rules | {Component}",
                    Volatile.Read(ref _embeddedSoFar), _totalToEmbed, "AKG");
            }
            finally
            {
                _isRebuilding = false;
                _currentRuleId = null;

                var detail = cancelled
                    ? "abgebrochen"
                    : failures > 0
                        ? $"{_embeddedSoFar} Regeln, {failures} fehlgeschlagen"
                        : $"{_embeddedSoFar} Regeln";
                _activity?.Report(ActivityKind.Embedding, ActivityState.Succeeded, detail);
                _activity?.Report(ActivityKind.Chunking, ActivityState.Succeeded, cancelled ? "abgebrochen" : null);
            }
        }
        finally
        {
            _rebuildCts = null;
            _rebuildGate.Release();
        }
    }

    /// <inheritdoc />
    public void CancelRebuild() => _rebuildCts?.Cancel();

    /// <inheritdoc />
    public async Task<(int Embedded, int Pending, int Failed, int Total)> GetCoverageAsync(CancellationToken ct)
    {
        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk)
            WITH r, count(c) AS chunks
            RETURN
                count(CASE WHEN chunks > 0 THEN 1 END) AS embedded,
                count(CASE WHEN chunks = 0 AND coalesce(r.embedAttempts, 0) < $maxAttempts THEN 1 END) AS pending,
                count(CASE WHEN chunks = 0 AND coalesce(r.embedAttempts, 0) >= $maxAttempts THEN 1 END) AS failed,
                count(r) AS total
            """,
            new { maxAttempts = MaxEmbedAttempts },
            ct).ConfigureAwait(false);

        if (rows.Count == 0) return (0, 0, 0, 0);
        var row = rows[0];
        int Get(string key) => row.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : 0;
        return (Get("embedded"), Get("pending"), Get("failed"), Get("total"));
    }

    /// <summary>
    /// Ensures the native Neo4j vector index over <c>(:RuleChunk).embedding</c> exists, sized to the
    /// embedding provider's dimensionality (<see cref="IEmbeddingService.Dimensions"/>). Best-effort:
    /// failures (e.g. graph providers without vector-index support such as Memgraph) are logged and
    /// swallowed — semantic search then falls back to app-side cosine similarity.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnsureVectorIndexAsync(CancellationToken ct)
    {
        var dimensions = _embeddings.Dimensions;
        if (dimensions <= 0)
            return;

        try
        {
            // Index OPTIONS cannot be parameterized in Cypher; the dimension is an internal int.
            await _cypher.ExecuteAsync(
                "CREATE VECTOR INDEX chunk_embeddings IF NOT EXISTS "
                + "FOR (c:RuleChunk) ON (c.embedding) "
                + "OPTIONS {indexConfig: {`vector.dimensions`: " + dimensions
                + ", `vector.similarity_function`: 'cosine'}}",
                ct: ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Vector index 'chunk_embeddings' ensured (dimensions={Dim}) | {Component}", dimensions, "AKG");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not ensure vector index (provider may not support vector indexes); "
                + "semantic search will use app-side cosine | {Component}", "AKG");
        }
    }

    /// <summary>
    /// Generates and persists chunk embeddings for a single rule if its body changed.
    /// No-op if <see cref="IEmbeddingService.IsAvailable"/> is <see langword="false"/>.
    /// </summary>
    /// <param name="ruleId">The rule ID to embed.</param>
    /// <param name="body">The rule body text to chunk and embed.</param>
    /// <param name="chunkStyle">Optional forced chunking style (null = auto-detect).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task EmbedSingleAsync(string ruleId, string body, string? chunkStyle, CancellationToken ct)
    {
        if (!_embeddings.IsAvailable) return;

        var hash = ComputeHash(body);
        if (await HasEmbeddingAsync(ruleId, hash, ct).ConfigureAwait(false)) return;

        await EmbedRuleAsync(ruleId, body, chunkStyle, ct).ConfigureAwait(false);
        _logger.LogDebug("Embedded rule '{RuleId}' | {Component}", ruleId, "AKG");
    }

    /// <summary>
    /// Checks whether a rule has current chunk embeddings for the given body hash.
    /// </summary>
    /// <param name="ruleId">The rule ID to check.</param>
    /// <param name="bodyHash">SHA-256 hash of the current rule body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the rule has at least one chunk and its stored body hash matches;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public async Task<bool> HasEmbeddingAsync(string ruleId, string bodyHash, CancellationToken ct)
    {
        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule {id: $ruleId})
            OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk)
            RETURN r.bodyHash AS hash, count(c) AS chunks
            """,
            new { ruleId },
            ct).ConfigureAwait(false);

        if (rows.Count == 0) return false;

        var row = rows[0];
        var chunkCount = row.TryGetValue("chunks", out var ch) ? Convert.ToInt32(ch) : 0;
        var storedHash = row.TryGetValue("hash", out var h) ? h?.ToString() : null;

        return chunkCount > 0 && storedHash == bodyHash;
    }

    /// <summary>
    /// Computes the SHA-256 hash of the given text as a hex string.
    /// </summary>
    /// <param name="body">Text to hash.</param>
    /// <returns>Uppercase hex-encoded SHA-256 hash.</returns>
    public static string ComputeHash(string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Records a failed embedding attempt by incrementing the rule's <c>embedAttempts</c> counter, so the
    /// backfill retries it on later cycles but eventually gives up (see <see cref="MaxEmbedAttempts"/>)
    /// instead of looping forever on a permanently-failing rule. Best-effort — never throws.
    /// </summary>
    /// <param name="ruleId">The rule whose failed attempt is recorded.</param>
    private async Task RecordEmbedFailureAsync(string ruleId)
    {
        try
        {
            await _cypher.ExecuteAsync(
                "MATCH (r:Rule {id: $id}) SET r.embedAttempts = coalesce(r.embedAttempts, 0) + 1",
                new { id = ruleId },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not record embed-failure attempt for rule '{RuleId}' | {Component}", ruleId, "AKG");
        }
    }

    /// <summary>
    /// Chunks the body, replaces the rule's existing chunks with freshly embedded ones, and records the
    /// body hash on the rule. The whole operation is keyed on the rule id, so it is safe to re-run.
    /// </summary>
    private async Task EmbedRuleAsync(string ruleId, string body, string? chunkStyle, CancellationToken ct)
    {
        var options = _chunkingOptions();
        var chunks = _chunker.Chunk(body, options, fileNameHint: ruleId, forcedStyle: chunkStyle);
        var texts = chunks.Select(c => c.Text).ToList();

        var vectors = await EmbedWithBackoffAsync(texts, ruleId, ct).ConfigureAwait(false);
        if (vectors.Count != chunks.Count)
        {
            _logger.LogWarning(
                "Embedding count {Vectors} != chunk count {Chunks} for rule '{RuleId}'; skipping | {Component}",
                vectors.Count, chunks.Count, ruleId, "AKG");
            return;
        }

        var hash = ComputeHash(body);
        var chunkMaps = new List<Dictionary<string, object?>>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            chunkMaps.Add(new Dictionary<string, object?>
            {
                ["ord"] = chunks[i].Ordinal,
                ["text"] = chunks[i].Text,
                ["style"] = chunks[i].Style,
                ["emb"] = vectors[i].Cast<object>().ToList(),
            });
        }

        // Replace existing chunks atomically per rule: delete old children, then recreate from scratch.
        await _cypher.ExecuteAsync(
            "MATCH (:Rule {id: $id})-[:HAS_CHUNK]->(c:RuleChunk) DETACH DELETE c",
            new { id = ruleId },
            ct).ConfigureAwait(false);

        await _cypher.ExecuteAsync(
            """
            MATCH (r:Rule {id: $id})
            SET r.bodyHash = $hash, r.embedAttempts = null
            WITH r
            UNWIND $chunks AS ch
            CREATE (r)-[:HAS_CHUNK]->(:RuleChunk {
                parentId: $id, ord: ch.ord, text: ch.text, style: ch.style,
                embedding: ch.emb, ownerId: r.ownerId
            })
            """,
            new { id = ruleId, hash, chunks = chunkMaps },
            ct).ConfigureAwait(false);

        // A file's chunks changed → its repository/upload head centroid is now stale (ADR-0009 stage 1).
        await MarkHeadDirtyAsync(ruleId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Embeds a rule's chunk texts, retrying a <em>transient</em> provider failure (rate limit, 5xx,
    /// network blip — any <see cref="ProviderException"/> that is not a <see cref="ProviderAuthException"/>)
    /// with an exponential backoff plus jitter between attempts (see <see cref="ExponentialBackoff"/>).
    /// Non-transient failures (auth errors, or any non-provider exception) propagate immediately so the
    /// caller records the attempt and moves on — retrying them would be futile. Waiting is driven through
    /// the injected <see cref="TimeProvider"/>, keeping the backoff deterministic under test.
    /// </summary>
    /// <param name="texts">The chunk texts to embed.</param>
    /// <param name="ruleId">The rule id, for diagnostic logging only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The embedding vectors for <paramref name="texts"/>.</returns>
    private async Task<IReadOnlyList<float[]>> EmbedWithBackoffAsync(
        IReadOnlyList<string> texts, string ruleId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _embeddings.EmbedBatchAsync(texts, ct).ConfigureAwait(false);
            }
            catch (ProviderException ex) when (ex is not ProviderAuthException && attempt < MaxTransientRetries)
            {
                var delay = ExponentialBackoff.WithJitter(
                    ExponentialBackoff.ComputeDelay(attempt, BaseRetryDelay, MaxRetryDelay),
                    RetryJitterFraction,
                    Random.Shared.NextDouble());

                _logger.LogWarning(ex,
                    "Transient embedding failure for rule '{RuleId}' (attempt {Attempt}/{Max}); "
                    + "retrying in {Delay} | {Component}",
                    ruleId, attempt + 1, MaxTransientRetries, delay, "AKG");

                await Task.Delay(delay, _timeProvider, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Flags the repository/upload head whose subtree contains <paramref name="ruleId"/> as needing its
    /// head-vector centroids recomputed. No-op for ids that are not nested file leaves. Best-effort.
    /// </summary>
    /// <param name="ruleId">The (re)embedded rule id, e.g. <c>git:repo:path</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task MarkHeadDirtyAsync(string ruleId, CancellationToken ct)
    {
        var headId = HeadIdOf(ruleId);
        if (headId is null)
            return;

        try
        {
            await _cypher.ExecuteAsync(
                "MATCH (h:Rule {id: $id}) SET h.headVectorDirty = true",
                new { id = headId },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not flag head '{HeadId}' dirty | {Component}", headId, "AKG");
        }
    }

    /// <summary>
    /// Derives the repository/upload head id from a nested file-leaf id (<c>git:repo:path</c> →
    /// <c>git:repo</c>, <c>upload:src:item</c> → <c>upload:src</c>), or <see langword="null"/> when the id
    /// is not such a leaf.
    /// </summary>
    /// <param name="ruleId">A rule id.</param>
    /// <returns>The two-segment head id, or <see langword="null"/>.</returns>
    private static string? HeadIdOf(string ruleId)
    {
        if (!ruleId.StartsWith("git:", StringComparison.Ordinal)
            && !ruleId.StartsWith("upload:", StringComparison.Ordinal))
            return null;

        var parts = ruleId.Split(':');
        return parts.Length >= 3 ? parts[0] + ":" + parts[1] : null;
    }
}
