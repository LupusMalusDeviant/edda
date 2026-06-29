using System.Runtime.CompilerServices;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.TestUtilities;

/// <summary>
/// Fake <see cref="IIngestionSource"/> that yields a fixed set of items. Used to drive the pipeline in
/// unit tests without a real source.
/// </summary>
internal sealed class FakeIngestionSource : IIngestionSource
{
    private readonly IReadOnlyList<IngestionItem> _items;

    public FakeIngestionSource(string sourceKind, params IngestionItem[] items)
    {
        SourceKind = sourceKind;
        _items = items;
    }

    public string SourceKind { get; }

    public async IAsyncEnumerable<IngestionItem> FetchAsync(
        IngestionSourceConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in _items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }

        await Task.CompletedTask;
    }
}
