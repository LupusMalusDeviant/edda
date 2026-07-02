using System.Diagnostics.CodeAnalysis;
using Edda.Core.Abstractions;
using Neo4j.Driver;

namespace Edda.AKG.Graph;

/// <summary>
/// Production implementation of <see cref="ICypherExecutor"/> backed by the Neo4j driver.
/// Excluded from code coverage — tested via integration tests only.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class Neo4jCypherExecutor : ICypherExecutor
{
    private readonly IDriver _driver;

    /// <summary>
    /// Initializes a new instance of <see cref="Neo4jCypherExecutor"/>.
    /// </summary>
    /// <param name="driver">The Neo4j driver instance.</param>
    public Neo4jCypherExecutor(IDriver driver) => _driver = driver;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string cypher,
        object? parameters = null,
        CancellationToken ct = default)
    {
        await using var session = _driver.AsyncSession();
        var cursor = await session.RunAsync(cypher, parameters);
        var records = await cursor.ToListAsync(ct);
        return records
            .Select(r => (IReadOnlyDictionary<string, object?>)r.Values
                .ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
            .ToList();
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(
        string cypher,
        object? parameters = null,
        CancellationToken ct = default)
    {
        await using var session = _driver.AsyncSession();
        var cursor = await session.RunAsync(cypher, parameters);
        // Consume the cursor so the write is deterministically flushed here, rather than relying on the
        // session's disposal to drain any pending result. (QueryAsync already drains via ToListAsync.)
        await cursor.ConsumeAsync();
    }
}
