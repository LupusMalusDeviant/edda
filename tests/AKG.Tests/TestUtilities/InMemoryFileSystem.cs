using Edda.Core.Abstractions;

namespace Edda.AKG.Tests.TestUtilities;

/// <summary>
/// In-memory implementation of <see cref="IFileSystem"/> for use in AKG unit tests.
/// All operations are performed against a Dictionary — no real disk I/O occurs.
/// </summary>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    internal void AddFile(string path, string content)
    {
        _files[path] = System.Text.Encoding.UTF8.GetBytes(content);
        EnsureDirectoryExists(GetDirectoryName(path));
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default)
    {
        if (!_files.TryGetValue(path, out var bytes))
            throw new FileNotFoundException($"File not found: {path}");
        return Task.FromResult(System.Text.Encoding.UTF8.GetString(bytes));
    }

    public Task WriteAllTextAsync(string path, string content, CancellationToken ct = default)
    {
        _files[path] = System.Text.Encoding.UTF8.GetBytes(content);
        EnsureDirectoryExists(GetDirectoryName(path));
        return Task.CompletedTask;
    }

    public Task AppendAllTextAsync(string path, string content, CancellationToken ct = default)
    {
        var existing = _files.TryGetValue(path, out var bytes)
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : string.Empty;
        _files[path] = System.Text.Encoding.UTF8.GetBytes(existing + content);
        EnsureDirectoryExists(GetDirectoryName(path));
        return Task.CompletedTask;
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        if (!_files.TryGetValue(path, out var bytes))
            throw new FileNotFoundException($"File not found: {path}");
        return Task.FromResult(bytes);
    }

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        _files[path] = bytes;
        EnsureDirectoryExists(GetDirectoryName(path));
        return Task.CompletedTask;
    }

    public bool FileExists(string path) => _files.ContainsKey(path);

    public bool DirectoryExists(string path) => _directories.Contains(path);

    public void EnsureDirectoryExists(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var current = path;
        while (!string.IsNullOrEmpty(current))
        {
            _directories.Add(current);
            var parent = GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }
    }

    public void DeleteFile(string path) => _files.Remove(path);

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, bool recursive = false)
    {
        var suffix = searchPattern.TrimStart('*');
        // Match files strictly *inside* the directory (prefix + separator) so that e.g. "knowledge"
        // does not also match files under the sibling "knowledge.seed" — mirrors real directory semantics.
        var prefix = path.EndsWith('/') ? path : path + "/";
        return _files.Keys.Where(k =>
            k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            (suffix == "" || k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));
    }

    public string CombinePath(params string[] parts) => string.Join("/", parts.Select(p => p.TrimEnd('/')));

    public string GetFullPath(string path) => path;

    private static string GetDirectoryName(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx < 0 ? string.Empty : path[..idx];
    }
}
