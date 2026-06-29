using Edda.Core.Abstractions;

namespace Edda.AKG.Tests.TestUtilities;

/// <summary>
/// Fake ICypherExecutor for unit tests. Supports per-query result configuration.
/// </summary>
internal sealed class FakeCypherExecutor : ICypherExecutor
{
    private readonly List<Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>> _queryHandlers = [];

    public List<string> ExecutedQueries { get; } = [];
    public List<string> ExecutedWriteQueries { get; } = [];

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> DefaultResult { get; set; }
        = Array.Empty<IReadOnlyDictionary<string, object?>>();

    public void AddQueryHandler(
        Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> handler)
        => _queryHandlers.Insert(0, handler);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string cypher,
        object? parameters = null,
        CancellationToken ct = default)
    {
        ExecutedQueries.Add(cypher);
        foreach (var handler in _queryHandlers)
        {
            var result = handler(cypher);
            if (result != DefaultResult)
                return Task.FromResult(result);
        }

        return Task.FromResult(DefaultResult);
    }

    public Task ExecuteAsync(
        string cypher,
        object? parameters = null,
        CancellationToken ct = default)
    {
        ExecutedWriteQueries.Add(cypher);
        return Task.CompletedTask;
    }
}
