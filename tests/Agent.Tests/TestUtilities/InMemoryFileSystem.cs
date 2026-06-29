using Edda.Core.Abstractions;

namespace Edda.Agent.Tests.TestUtilities;

/// <summary>
/// In-memory implementation of IFileSystem for use in Agent unit tests.
/// All operations are performed against a Dictionary — no real disk I/O occurs.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pre-populates a file with text content.</summary>
    public void AddFile(string path, string content)
    {
        _files[path] = System.Text.Encoding.UTF8.GetBytes(content);
        EnsureDirectoryExists(GetDirectoryName(path));
    }

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default)
    {
        if (!_files.TryGetValue(path, out var bytes))
            throw new FileNotFoundException($"File not found: {path}");
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
            throw new FileNotFoundException($"File not found: {path}");
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
    /// <remarks>
    /// Creates the directory and all ancestor directories, matching the behavior of
    /// <c>Directory.CreateDirectory</c> in PhysicalFileSystem.
    /// </remarks>
    public void EnsureDirectoryExists(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        // Register the directory and every ancestor segment so that callers that
        // check a parent directory (e.g. "data/users") after writing to a child
        // directory (e.g. "data/users/user1") find it already registered.
        var current = path;
        while (!string.IsNullOrEmpty(current))
        {
            _directories.Add(current);
            var parent = GetDirectoryName(current);
            if (parent == current) break; // Guard against infinite loop at root
            current = parent;
        }
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
