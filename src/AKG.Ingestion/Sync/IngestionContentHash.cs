using System.Security.Cryptography;
using System.Text;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Sync;

/// <summary>
/// Deterministic content signature of an <see cref="IngestionItem"/> for incremental-sync change detection
/// (C5). Hashes the rule-affecting source fields (title, body, domain, tags, native links, chunk style) so an
/// unchanged item yields an unchanged hash across runs. LLM-free, no time/randomness.
/// </summary>
internal static class IngestionContentHash
{
    /// <summary>Computes the content hash of <paramref name="item"/> (lowercase hex SHA-256).</summary>
    /// <param name="item">The ingestion item to sign.</param>
    /// <returns>A 64-character lowercase hex SHA-256 digest.</returns>
    public static string Compute(IngestionItem item)
    {
        var sb = new StringBuilder();
        sb.Append(item.Title).Append('\n');
        sb.Append(item.Body).Append('\n');
        sb.Append(item.Domain ?? string.Empty).Append('\n');
        sb.Append(item.ChunkStyle ?? string.Empty).Append('\n');
        foreach (var tag in item.Tags)                       // Tags are source-ordered + deterministic
            sb.Append(tag).Append(',');
        sb.Append('\n');
        foreach (var link in item.NativeLinks)               // NativeLinks are source-ordered + deterministic
            sb.Append(link.Kind).Append('>').Append(link.TargetRef).Append(';');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
