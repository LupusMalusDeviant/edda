using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Evaluation;

/// <summary>
/// Default <see cref="IExtractionEvaluator"/>: runs each case through the extractor and scores the produced
/// entities/relations against the golden reference. Entities are matched by normalized name; relations by the
/// normalized (source, target) pair (descriptions and keywords are advisory and not scored). Matching is
/// set-based, so duplicate extractions do not inflate the counts. Stateless and provider-agnostic — the same
/// evaluator scores a mocked extractor (deterministic tests) or a real LLM-backed one (Ollama).
/// </summary>
public sealed class ExtractionEvaluator : IExtractionEvaluator
{
    /// <inheritdoc />
    public async Task<ExtractionEvalReport> EvaluateAsync(
        IEntityExtractor extractor,
        ExtractionEvalDataset dataset,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ExtractionEvalCaseResult>(dataset.Cases.Count);

        foreach (var evalCase in dataset.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var actual = await extractor
                .ExtractAsync(evalCase.Text, evalCase.DomainHint, cancellationToken)
                .ConfigureAwait(false);

            results.Add(new ExtractionEvalCaseResult
            {
                CaseId = evalCase.Id,
                EntityScore = ScoreEntities(actual.Entities, evalCase.Expected.Entities),
                RelationScore = ScoreRelations(actual.Relations, evalCase.Expected.Relations),
                Actual = actual,
            });
        }

        return new ExtractionEvalReport
        {
            DatasetName = dataset.Name,
            CaseCount = results.Count,
            EntityScore = ExtractionScore.Mean(results.Select(r => r.EntityScore).ToList()),
            RelationScore = ExtractionScore.Mean(results.Select(r => r.RelationScore).ToList()),
            Cases = results,
        };
    }

    private static ExtractionScore ScoreEntities(
        IReadOnlyList<ExtractedEntity> predicted, IReadOnlyList<ExtractedEntity> golden)
        => Score(predicted.Select(e => Norm(e.Name)), golden.Select(e => Norm(e.Name)));

    private static ExtractionScore ScoreRelations(
        IReadOnlyList<ExtractedRelation> predicted, IReadOnlyList<ExtractedRelation> golden)
        => Score(
            predicted.Select(r => (Norm(r.Source), Norm(r.Target))),
            golden.Select(r => (Norm(r.Source), Norm(r.Target))));

    // Set-based precision/recall/F1: a predicted key is a true positive iff it is also a golden key.
    private static ExtractionScore Score<T>(IEnumerable<T> predicted, IEnumerable<T> golden)
    {
        var predictedSet = predicted.ToHashSet();
        var goldenSet = golden.ToHashSet();
        var truePositives = predictedSet.Count(goldenSet.Contains);
        return ExtractionScore.Compute(truePositives, predictedSet.Count, goldenSet.Count);
    }

    private static string Norm(string value) => value.Trim().ToLowerInvariant();
}
