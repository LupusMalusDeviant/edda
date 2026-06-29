using Edda.Core.Abstractions;

namespace Edda.AKG.Ingestion.Tests.TestUtilities;

/// <summary>
/// In-memory implementation of <see cref="IFileSystem"/> for unit tests. All operations run against a
/// dictionary — no real disk I/O occurs.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pre-populates a file with text content.</summary>
    public void AddFile(string path, string content)
    {
        _files[path] = System.Text.Encoding.UTF8.GetBytes(content);
        EnsureDirectoryExists(GetDirectoryName(path));
    }

    /// <summary>
    /// Adds a file that is enumerated but throws on read — simulates a dangling symlink (e.g. a
    /// build-generated <c>compile_commands.json</c> whose target is not in the clone).
    /// </summary>
    public void AddUnreadableFile(string path)
    {
        _files[path] = [];
        _unreadable.Add(path);
        EnsureDirectoryExists(GetDirectoryName(path));
    }

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default)
    {
        if (_unreadable.Contains(path))
            throw new FileNotFoundException($"Unreadable (dangling) file in InMemoryFileSystem: {path}");
        if (!_files.TryGetValue(path, out var bytes))
            throw new FileNotFoundException($"File not found in InMemoryFileSystem: {path}");
        return Task.FromResult(System.Text.Encoding.UTF8.GetString(bytes));
    }

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string content, CancellationToken ct = default)
    {
        _files[path] = System.Text.Encoding.UTF8.GetBytes(content);
        EnsureDirectoryExists(GetDirectoryName(path));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AppendAllTextAsync(string path, string content, CancellationToken ct = default)
    {
        var existing = _files.TryGetValue(path, out var bytes)
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : string.Empty;
        _files[path] = System.Text.Encoding.UTF8.GetBytes(existing + content);
        EnsureDirectoryExists(GetDirectoryName(path));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        if (!_files.TryGetValue(path, out var bytes))
            throw new FileNotFoundException($"File not found in InMemoryFileSystem: {path}");
        return Task.FromResult(bytes);
    }

    /// <inheritdoc />
    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        _files[path] = bytes;
        EnsureDirectoryExists(GetDirectoryName(path));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool FileExists(string path) => _files.ContainsKey(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => _directories.Contains(path);

    /// <inheritdoc />
    public void EnsureDirectoryExists(string path)
    {
        if (!string.IsNullOrEmpty(path))
            _directories.Add(path);
    }

    /// <inheritdoc />
    public void DeleteFile(string path) => _files.Remove(path);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, bool recursive = false)
    {
        var suffix = searchPattern.TrimStart('*');
        return _files.Keys.Where(k =>
            k.StartsWith(path, StringComparison.OrdinalIgnoreCase) &&
            (suffix == "" || k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));
    }

    /// <inheritdoc />
    public string CombinePath(params string[] parts) => string.Join("/", parts.Select(p => p.TrimEnd('/')));

    /// <inheritdoc />
    public string GetFullPath(string path) => path;

    private static string GetDirectoryName(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx < 0 ? string.Empty : path[..idx];
    }
}
