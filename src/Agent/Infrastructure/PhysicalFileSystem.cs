using Edda.Core.Abstractions;

namespace Edda.Agent.Infrastructure;

/// <summary>
/// Production implementation of <see cref="IFileSystem"/> that delegates to <c>System.IO</c>.
/// This is the only class in the codebase permitted to call <c>File.*</c>, <c>Directory.*</c>,
/// and <c>Path.*</c> directly. All other code must use <see cref="IFileSystem"/> via DI.
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default)
        => File.ReadAllTextAsync(path, ct);

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string content, CancellationToken ct = default)
        => File.WriteAllTextAsync(path, content, ct);

    /// <inheritdoc />
    public Task AppendAllTextAsync(string path, string content, CancellationToken ct = default)
        => File.AppendAllTextAsync(path, content, ct);

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
        => File.ReadAllBytesAsync(path, ct);

    /// <inheritdoc />
    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
        => File.WriteAllBytesAsync(path, bytes, ct);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(
        string path, string searchPattern, bool recursive = false)
        => Directory.Exists(path)
            ? Directory.EnumerateFiles(
                path,
                searchPattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            : [];

    /// <inheritdoc />
    public string CombinePath(params string[] parts) => Path.Combine(parts);

    /// <inheritdoc />
    public string GetFullPath(string path) => Path.GetFullPath(path);
}
