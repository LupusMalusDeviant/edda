namespace Edda.Core.Abstractions;

/// <summary>
/// Abstracts all filesystem operations for full testability.
/// This is the only permitted location for direct file I/O in the entire system.
/// Production: PhysicalFileSystem (wraps System.IO). Tests: InMemoryFileSystem (Dictionary-based).
/// No code outside of PhysicalFileSystem may use File.*, Directory.*, or Path.* directly.
/// </summary>
public interface IFileSystem
{
    /// <summary>Reads all text from a file asynchronously.</summary>
    /// <param name="path">Absolute or relative path to the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The file content as a string.</returns>
    Task<string> ReadAllTextAsync(string path, CancellationToken ct = default);

    /// <summary>Writes text to a file, overwriting any existing content.</summary>
    Task WriteAllTextAsync(string path, string content, CancellationToken ct = default);

    /// <summary>Appends text to a file, creating it if it does not exist.</summary>
    Task AppendAllTextAsync(string path, string content, CancellationToken ct = default);

    /// <summary>Reads all bytes from a file asynchronously.</summary>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default);

    /// <summary>Writes bytes to a file, overwriting any existing content.</summary>
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default);

    /// <summary>Returns true if the specified file exists.</summary>
    bool FileExists(string path);

    /// <summary>Returns true if the specified directory exists.</summary>
    bool DirectoryExists(string path);

    /// <summary>Creates the directory and all parent directories if they do not exist.</summary>
    void EnsureDirectoryExists(string path);

    /// <summary>Deletes the specified file. No-op if the file does not exist.</summary>
    void DeleteFile(string path);

    /// <summary>
    /// Enumerates files matching a search pattern in the specified directory.
    /// </summary>
    /// <param name="path">Root directory to search.</param>
    /// <param name="searchPattern">Glob-style search pattern (e.g. "*.md").</param>
    /// <param name="recursive">True to search subdirectories recursively.</param>
    /// <returns>Absolute paths of matching files.</returns>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, bool recursive = false);

    /// <summary>Combines multiple path segments into a single path.</summary>
    string CombinePath(params string[] parts);

    /// <summary>Returns the absolute path for the given relative or absolute path.</summary>
    string GetFullPath(string path);
}
