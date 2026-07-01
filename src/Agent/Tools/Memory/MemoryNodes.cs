using System.Security.Cryptography;
using System.Text;
using Edda.Core.Models;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Shared construction of episodic-memory graph nodes (M3 / ADR-0011): a memory is a per-fact
/// <see cref="KnowledgeRule"/> with <c>SourceType=memory</c>, owned by a single user. A deterministic
/// content-hash id makes remembering the same fact idempotent (upsert, no duplicate) and lets
/// <c>forget</c> address a memory by its content alone.
/// </summary>
internal static class MemoryNodes
{
    /// <summary>Rule type discriminator for episodic memories.</summary>
    internal const string MemoryType = "Memory";

    /// <summary>Domain grouping all episodic memories.</summary>
    internal const string MemoryDomain = "memory";

    /// <summary>Provenance label for episodic memories (a known <see cref="KnowledgeRule.SourceType"/>).</summary>
    internal const string MemorySourceType = "memory";

    /// <summary>
    /// Builds the deterministic node id for a user's memory of <paramref name="content"/>. The id is derived
    /// from the owner and a hash of the trimmed content, so the same fact always maps to the same node
    /// (idempotent remember / content-addressable forget).
    /// </summary>
    /// <param name="userId">Owner of the memory.</param>
    /// <param name="content">The remembered fact.</param>
    /// <returns>A stable node id of the form <c>memory-{userId}-{hash}</c>.</returns>
    internal static string NodeId(string userId, string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content.Trim()));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"memory-{userId}-{hex[..16]}";
    }

    /// <summary>
    /// Creates a memory rule for <paramref name="content"/>, owned by <paramref name="userId"/> and
    /// timestamped with <paramref name="createdUtc"/>.
    /// </summary>
    /// <param name="userId">Owner of the memory.</param>
    /// <param name="content">The remembered fact (stored as the rule body).</param>
    /// <param name="createdUtc">Creation time from the injected <see cref="TimeProvider"/>.</param>
    /// <returns>The memory rule ready to upsert.</returns>
    internal static KnowledgeRule Create(string userId, string content, DateTimeOffset createdUtc) =>
        new()
        {
            Id = NodeId(userId, content),
            Type = MemoryType,
            Domain = MemoryDomain,
            Priority = RulePriority.Low,
            Confidence = 1.0,
            Tags = ["memory", userId],
            Author = userId,
            Created = DateOnly.FromDateTime(createdUtc.UtcDateTime),
            Body = content.Trim(),
            OwnerId = userId,
            SourceType = MemorySourceType,
        };
}
