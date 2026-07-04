using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Authorization;

/// <summary>
/// Pass-through <see cref="IDatasetWriteAuthorizer"/> (ADR-0014): no dataset awareness — it delegates straight
/// to the wrapped sync <see cref="IRuleAuthorizer"/>. The behaviour-neutral default used whenever dataset
/// permissions are disabled, so rule mutations keep the exact pre-dataset C2 semantics.
/// </summary>
internal sealed class PassThroughDatasetWriteAuthorizer : IDatasetWriteAuthorizer
{
    private readonly IRuleAuthorizer _inner;

    /// <summary>Initializes a new <see cref="PassThroughDatasetWriteAuthorizer"/> over the C2 authorizer.</summary>
    /// <param name="inner">The sync rule authorizer every call delegates to.</param>
    public PassThroughDatasetWriteAuthorizer(IRuleAuthorizer inner) => _inner = inner;

    /// <inheritdoc />
    public Task EnsureCanMutateAsync(
        KnowledgeRule rule, string userId, bool isAdmin = false, CancellationToken cancellationToken = default)
    {
        _inner.EnsureCanMutate(rule, userId, isAdmin);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EnsureCanMutateAsync(
        string ruleId, string? ownerId, string userId, bool isAdmin = false, CancellationToken cancellationToken = default)
    {
        _inner.EnsureCanMutate(ownerId, userId, isAdmin);
        return Task.CompletedTask;
    }
}
