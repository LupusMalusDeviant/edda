using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Scores an <see cref="IEntityExtractor"/> against a curated golden dataset: runs each case's text through the
/// extractor and compares the produced entities/relations to the expected ones, yielding precision/recall/F1.
/// Provider-agnostic — the same evaluator scores a mocked extractor (deterministic unit tests) or a real
/// LLM-backed extractor (e.g. Ollama) for actual extraction-quality measurement.
/// </summary>
public interface IExtractionEvaluator
{
    /// <summary>
    /// Runs every case in <paramref name="dataset"/> through <paramref name="extractor"/> and scores the
    /// produced entities and relations against each case's golden reference.
    /// </summary>
    /// <param name="extractor">The entity extractor under evaluation.</param>
    /// <param name="dataset">The golden cases to evaluate against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A report with per-case scores and the dataset-level entity/relation aggregates.</returns>
    Task<ExtractionEvalReport> EvaluateAsync(
        IEntityExtractor extractor,
        ExtractionEvalDataset dataset,
        CancellationToken cancellationToken = default);
}
