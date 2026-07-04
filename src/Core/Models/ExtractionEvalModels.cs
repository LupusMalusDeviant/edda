namespace Edda.Core.Models;

/// <summary>
/// A single entity-extraction evaluation case: source text plus the entities and relations a good extraction
/// should surface (the golden reference).
/// </summary>
public sealed record ExtractionEvalCase
{
    /// <summary>Stable identifier for the case, echoed into the report.</summary>
    public required string Id { get; init; }

    /// <summary>The source text handed to the extractor.</summary>
    public required string Text { get; init; }

    /// <summary>Optional domain hint passed through to the extractor.</summary>
    public string? DomainHint { get; init; }

    /// <summary>Ground-truth entities and relations for this text.</summary>
    public required EntityExtractionResult Expected { get; init; }
}

/// <summary>A named collection of entity-extraction evaluation cases.</summary>
public sealed record ExtractionEvalDataset
{
    /// <summary>Human-readable dataset name, echoed into the report.</summary>
    public required string Name { get; init; }

    /// <summary>The cases to evaluate.</summary>
    public IReadOnlyList<ExtractionEvalCase> Cases { get; init; } = [];
}

/// <summary>Precision / recall / F1 for a single facet (entities or relations), in [0, 1].</summary>
public sealed record ExtractionScore
{
    /// <summary>Fraction of predicted items that are correct (true positives / predicted).</summary>
    public double Precision { get; init; }

    /// <summary>Fraction of golden items that were found (true positives / golden).</summary>
    public double Recall { get; init; }

    /// <summary>Harmonic mean of precision and recall.</summary>
    public double F1 { get; init; }

    /// <summary>
    /// Computes the score from set counts. Both sets empty scores 1.0 (correctly predicting nothing); an empty
    /// denominator otherwise scores 0.0 for that component.
    /// </summary>
    /// <param name="truePositives">Number of predicted items that are also golden.</param>
    /// <param name="predictedCount">Number of distinct predicted items.</param>
    /// <param name="goldenCount">Number of distinct golden items.</param>
    /// <returns>The precision/recall/F1 triple.</returns>
    public static ExtractionScore Compute(int truePositives, int predictedCount, int goldenCount)
    {
        if (predictedCount == 0 && goldenCount == 0)
            return new ExtractionScore { Precision = 1.0, Recall = 1.0, F1 = 1.0 };

        var precision = predictedCount == 0 ? 0.0 : (double)truePositives / predictedCount;
        var recall = goldenCount == 0 ? 0.0 : (double)truePositives / goldenCount;
        var f1 = precision + recall == 0.0 ? 0.0 : 2.0 * precision * recall / (precision + recall);
        return new ExtractionScore { Precision = precision, Recall = recall, F1 = f1 };
    }

    /// <summary>Macro-averages a set of per-case scores (mean of each component). Empty input scores 0.</summary>
    /// <param name="scores">The per-case scores to average.</param>
    /// <returns>The mean score.</returns>
    public static ExtractionScore Mean(IReadOnlyList<ExtractionScore> scores)
    {
        if (scores.Count == 0)
            return new ExtractionScore();

        return new ExtractionScore
        {
            Precision = scores.Average(s => s.Precision),
            Recall = scores.Average(s => s.Recall),
            F1 = scores.Average(s => s.F1),
        };
    }
}

/// <summary>Per-case extraction evaluation outcome.</summary>
public sealed record ExtractionEvalCaseResult
{
    /// <summary>The originating case id.</summary>
    public required string CaseId { get; init; }

    /// <summary>Entity-level score (matched by normalized name).</summary>
    public required ExtractionScore EntityScore { get; init; }

    /// <summary>Relation-level score (matched by normalized source→target pair).</summary>
    public required ExtractionScore RelationScore { get; init; }

    /// <summary>What the extractor actually produced for this case.</summary>
    public required EntityExtractionResult Actual { get; init; }
}

/// <summary>Full entity-extraction evaluation report for a dataset run.</summary>
public sealed record ExtractionEvalReport
{
    /// <summary>The evaluated dataset's name.</summary>
    public required string DatasetName { get; init; }

    /// <summary>Number of cases evaluated.</summary>
    public int CaseCount { get; init; }

    /// <summary>Macro-averaged entity score across all cases.</summary>
    public required ExtractionScore EntityScore { get; init; }

    /// <summary>Macro-averaged relation score across all cases.</summary>
    public required ExtractionScore RelationScore { get; init; }

    /// <summary>Per-case results in dataset order.</summary>
    public IReadOnlyList<ExtractionEvalCaseResult> Cases { get; init; } = [];
}
