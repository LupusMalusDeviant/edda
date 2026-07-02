using Microsoft.AspNetCore.Mvc.Testing;

namespace Edda.Hosting.Tests.Infrastructure;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that boots the Edda web host with the zero-infrastructure
/// providers (<c>GRAPH_PROVIDER=memory</c>, <c>EMBEDDING_PROVIDER=null</c>) so integration tests need no
/// Docker/Neo4j. Because Program reads these — and EDDA_AUTH_TOKEN / MCP_SERVER_ENABLED / FEEDBACK_DB_PATH —
/// from environment variables before the host is built, they are applied as real env vars for the factory's
/// lifetime and restored on dispose (tests run serially, see AssemblyInfo). The SQLite feedback store is
/// pointed at a unique temp file so it never touches the repository's <c>data/</c> directory.
/// </summary>
internal sealed class HostingTestFactory : WebApplicationFactory<Program>
{
    private readonly List<(string Key, string? Original)> _saved = [];
    private readonly string _feedbackDbPath;

    /// <summary>Creates a factory, applying the zero-infra defaults plus any per-test env overrides.</summary>
    /// <param name="env">Additional environment variables (e.g. EDDA_AUTH_TOKEN, MCP_SERVER_ENABLED, EDDA_BIND).</param>
    public HostingTestFactory(params (string Key, string? Value)[] env)
    {
        _feedbackDbPath = Path.Combine(Path.GetTempPath(), $"edda-hosting-tests-{Guid.NewGuid():N}.db");

        SetEnv("GRAPH_PROVIDER", "memory");
        SetEnv("EMBEDDING_PROVIDER", "null");
        SetEnv("FEEDBACK_DB_PATH", _feedbackDbPath);
        foreach (var (key, value) in env)
            SetEnv(key, value);
    }

    private void SetEnv(string key, string? value)
    {
        _saved.Add((key, Environment.GetEnvironmentVariable(key)));
        Environment.SetEnvironmentVariable(key, value);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        // Restore in reverse order so a key set twice returns to its true original.
        for (var i = _saved.Count - 1; i >= 0; i--)
            Environment.SetEnvironmentVariable(_saved[i].Key, _saved[i].Original);

        try
        {
            if (File.Exists(_feedbackDbPath))
                File.Delete(_feedbackDbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup — the OS reclaims the temp file eventually.
        }
    }
}
