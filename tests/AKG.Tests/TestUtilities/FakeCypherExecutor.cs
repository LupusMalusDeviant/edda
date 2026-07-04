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

    /// <summary>Parameter objects passed to each read query, parallel to <see cref="ExecutedQueries"/>.</summary>
    public List<object?> ExecutedParameters { get; } = [];

    /// <summary>Parameter objects passed to each write, parallel to <see cref="ExecutedWriteQueries"/>.</summary>
    public List<object?> ExecutedWriteParameters { get; } = [];

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
        ExecutedParameters.Add(parameters);
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
        ExecutedWriteParameters.Add(parameters);
        return Task.CompletedTask;
    }
}
