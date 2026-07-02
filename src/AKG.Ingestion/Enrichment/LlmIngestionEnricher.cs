using System.Text;
using System.Text.Json;
using Edda.AKG.Ingestion.Llm;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Edda.Core.Models;
using Edda.Core.Resilience;
using Edda.Security.OutputFilter;
using Edda.Security.Sanitization;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Ingestion.Enrichment;

/// <summary>
/// Optional LLM-backed <see cref="IIngestionEnricher"/> (see ADR-0001): condenses an item's body into a
/// concise note and proposes semantic relations. To keep the graph consistent it only ever proposes
/// relations to ids already present in <c>knownIds</c> — never invents nodes. Best-effort: any LLM or
/// parsing failure leaves the item unchanged so enrichment never breaks ingestion.
/// </summary>
public sealed class LlmIngestionEnricher : IIngestionEnricher
{
    private const int MaxBodyChars = 6000;

    /// <summary>Retries for a <em>transient</em> LLM failure (429/5xx/timeout) before giving up.</summary>
    private const int MaxRetries = 2;

    /// <summary>Delay before the first transient retry; doubles per attempt up to <see cref="MaxRetryDelay"/>.</summary>
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound on a single transient-retry backoff delay.</summary>
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(20);

    private const string SystemPrompt =
        "You enrich knowledge-base documents. You receive one document (title + content) and a list of " +
        "candidate ids of OTHER documents. Respond with ONLY a JSON object of the form " +
        "{\"summary\": string, \"related\": [string]}. \"summary\" is a concise 1-3 sentence summary of the " +
        "document. \"related\" lists the candidate ids that are semantically related — choose ONLY from the " +
        "provided candidate ids and never invent ids. If unsure, use an empty array. Output JSON only.";

    /// <summary>System prompt for the single repair attempt after an unparseable response — stricter about format.</summary>
    private const string RepairSystemPrompt = SystemPrompt +
        "\n\nIMPORTANT: your previous reply was NOT valid JSON of the required form. Reply with ONLY the JSON " +
        "object {\"summary\": string, \"related\": [string]} — no prose, no explanations, no markdown code fences.";

    private readonly ILlmChatClient _chat;
    private readonly IInputSanitizer _sanitizer;
    private readonly ISecretRedactor _redactor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LlmIngestionEnricher> _logger;

    /// <summary>Initializes a new instance of the <see cref="LlmIngestionEnricher"/> class.</summary>
    /// <param name="chat">The chat client used for completions.</param>
    /// <param name="sanitizer">Neutralizes prompt-injection patterns in ingested content before it reaches the model.</param>
    /// <param name="redactor">Redacts secrets from ingested content before it reaches the model.</param>
    /// <param name="timeProvider">Clock used to delay between transient-failure retries (injectable for tests).</param>
    /// <param name="logger">Logger for best-effort diagnostics.</param>
    public LlmIngestionEnricher(
        ILlmChatClient chat,
        IInputSanitizer sanitizer,
        ISecretRedactor redactor,
        TimeProvider timeProvider,
        ILogger<LlmIngestionEnricher> logger)
    {
        _chat = chat;
        _sanitizer = sanitizer;
        _redactor = redactor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IngestionItem> EnrichAsync(
        IngestionItem item,
        IReadOnlyCollection<string> knownIds,
        CancellationToken cancellationToken = default)
    {
        var known = knownIds as IReadOnlySet<string> ?? knownIds.ToHashSet(StringComparer.Ordinal);
        var candidates = known.Where(id => !string.Equals(id, item.Id, StringComparison.Ordinal)).ToList();
        var userPrompt = BuildUserPrompt(item, candidates);

        var response = await CompleteWithRetryAsync(SystemPrompt, userPrompt, item.Id, cancellationToken)
            .ConfigureAwait(false);
        if (response is null)
            return item; // gave up (non-transient, or transient retries exhausted) — already logged

        if (!TryParse(response, out var summary, out var related))
        {
            // The response was not valid JSON of the expected shape. Make ONE repair attempt with a stricter
            // instruction before giving up, so a single formatting slip does not lose the enrichment.
            var repair = await CompleteWithRetryAsync(RepairSystemPrompt, userPrompt, item.Id, cancellationToken)
                .ConfigureAwait(false);
            if (repair is null || !TryParse(repair, out summary, out related))
            {
                _logger.LogWarning(
                    "Enrichment skipped for '{RuleId}' — invalid LLM JSON after one repair attempt | AKG", item.Id);
                return item;
            }
        }

        var proposedLinks = related
            .Where(known.Contains)
            .Where(id => !string.Equals(id, item.Id, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(id => item.NativeLinks.All(link => !string.Equals(link.TargetRef, id, StringComparison.Ordinal)))
            .Select(id => new IngestionLink { Kind = "related", TargetRef = id });

        return item with
        {
            Body = string.IsNullOrWhiteSpace(summary) ? item.Body : summary.Trim(),
            NativeLinks = item.NativeLinks.Concat(proposedLinks).ToList(),
        };
    }

    /// <summary>
    /// Calls the chat client, retrying a <em>transient</em> failure (rate limit / 5xx / request timeout) up
    /// to <see cref="MaxRetries"/> times with an exponential backoff between attempts. A non-transient error
    /// (or exhausted retries) is logged and yields <see langword="null"/> so the caller leaves the item
    /// unchanged (best-effort enrichment must never break ingestion). Waiting is driven through the injected
    /// <see cref="TimeProvider"/>, keeping the backoff deterministic under test.
    /// </summary>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="userPrompt">The user prompt.</param>
    /// <param name="itemId">The item id, for diagnostic logging only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The model response, or <see langword="null"/> when the call ultimately failed.</returns>
    private async Task<string?> CompleteWithRetryAsync(
        string systemPrompt, string userPrompt, string itemId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _chat.CompleteAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
            }
            catch (ProviderException ex) when (IsTransient(ex) && attempt < MaxRetries)
            {
                var delay = ExponentialBackoff.ComputeDelay(attempt, BaseRetryDelay, MaxRetryDelay);
                _logger.LogWarning(
                    "Enrichment transient LLM error for '{RuleId}' (attempt {Attempt}/{Total}, status {Status}); "
                    + "retrying in {Delay} | AKG",
                    itemId, attempt + 1, MaxRetries + 1, ex.StatusCode, delay);
                await Task.Delay(delay, _timeProvider, ct).ConfigureAwait(false);
            }
            catch (ProviderException ex)
            {
                _logger.LogWarning(
                    "Enrichment skipped for '{RuleId}' — LLM unavailable ({Message}) | AKG", itemId, ex.Message);
                return null;
            }
        }
    }

    /// <summary>
    /// Classifies a provider error as transient (worth retrying): a rate limit (429) or a 5xx / request-timeout
    /// status. Auth errors, bad requests and unknown/absent statuses are treated as non-transient.
    /// </summary>
    private static bool IsTransient(ProviderException ex)
        => ex is ProviderRateLimitException || ex.StatusCode is 408 or 500 or 502 or 503 or 504;

    private string BuildUserPrompt(IngestionItem item, IReadOnlyList<string> candidateIds)
    {
        var truncated = item.Body.Length > MaxBodyChars ? item.Body[..MaxBodyChars] : item.Body;
        var title = Clean(item.Title);
        var body = Clean(truncated);
        var sb = new StringBuilder();
        sb.Append("Document id: ").Append(item.Id).Append('\n');
        sb.Append("Title: ").Append(title).Append('\n').Append('\n');
        sb.Append("Content:\n").Append(body).Append('\n').Append('\n');
        sb.Append("Candidate ids:\n");
        foreach (var id in candidateIds)
            sb.Append(id).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Redacts secrets and neutralizes prompt-injection patterns in ingested free text before it is
    /// embedded into a prompt and sent to the language model (see PRD-0001 FR-07). Secrets are redacted
    /// first so they never reach the model, then injection markers are filtered from the result.
    /// </summary>
    /// <param name="raw">The raw ingested text (title or body).</param>
    /// <returns>The redacted and sanitized text safe to include in a prompt.</returns>
    private string Clean(string raw) => _sanitizer.Sanitize(_redactor.Redact(raw)).Text;

    /// <summary>
    /// Extracts <c>summary</c> and <c>related</c> from the model response, tolerating surrounding prose or
    /// code fences. Internal for unit testing. Returns false when no JSON object can be parsed.
    /// </summary>
    internal static bool TryParse(string response, out string summary, out IReadOnlyList<string> related)
    {
        summary = string.Empty;
        related = [];

        if (string.IsNullOrWhiteSpace(response))
            return false;

        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start)
            return false;

        try
        {
            using var document = JsonDocument.Parse(response[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("summary", out var summaryElement)
                && summaryElement.ValueKind == JsonValueKind.String)
            {
                summary = summaryElement.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("related", out var relatedElement)
                && relatedElement.ValueKind == JsonValueKind.Array)
            {
                var ids = new List<string>();
                foreach (var element in relatedElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var id = element.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                            ids.Add(id);
                    }
                }

                related = ids;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
