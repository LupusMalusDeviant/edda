using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Persists trigger definitions for the scheduling subsystem.
/// All operations are user-scoped.
/// </summary>
public interface ITriggerStore
{
    /// <summary>
    /// Returns all triggers belonging to a user.
    /// </summary>
    /// <param name="userId">The owning user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All triggers for the user, including disabled ones.</returns>
    Task<IReadOnlyList<TriggerDefinition>> GetAllAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a trigger definition.
    /// </summary>
    /// <param name="trigger">The trigger to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The saved trigger with any system-assigned defaults applied.</returns>
    Task<TriggerDefinition> SaveAsync(
        TriggerDefinition trigger,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a trigger. Only the owning user can delete their triggers.
    /// </summary>
    /// <param name="triggerId">The trigger ID to delete.</param>
    /// <param name="userId">Must match the trigger's owner.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(
        string triggerId,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Enables or disables a trigger without deleting it.
    /// </summary>
    /// <param name="triggerId">The trigger to modify.</param>
    /// <param name="userId">Must match the trigger's owner.</param>
    /// <param name="enabled">True to enable, false to disable.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetEnabledAsync(
        string triggerId,
        string userId,
        bool enabled,
        CancellationToken ct = default);
}
