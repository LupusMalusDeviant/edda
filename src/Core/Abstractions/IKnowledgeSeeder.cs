using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Seeds the runtime knowledge directory from a bundled fallback location.
/// Used in container deployments (Coolify, Kubernetes) where the knowledge
/// directory is backed by an empty named volume on first start — the seed
/// directory is baked into the image and copied over once.
/// </summary>
/// <remarks>
/// The seeder is idempotent: if the target directory already contains files
/// (whether seeded previously or operator-managed), no copy is performed.
/// This makes it safe to call on every startup.
/// </remarks>
public interface IKnowledgeSeeder
{
    /// <summary>
    /// Copies files from the seed source to the target knowledge directory
    /// if and only if the target is currently empty.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="KnowledgeSeedResult"/> indicating whether seeding ran,
    /// how many files were copied, and the reason for the outcome.
    /// </returns>
    Task<KnowledgeSeedResult> SeedIfEmptyAsync(CancellationToken ct = default);
}
