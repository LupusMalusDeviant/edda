namespace Edda.Core.Abstractions;

/// <summary>
/// Resolves the appropriate delivery channel name from a user ID prefix.
/// The userId convention (telegram:{chatId}, matrix:{roomId}, web:{username})
/// naturally encodes the originating channel.
/// Used to auto-populate delivery channels when none are explicitly specified.
/// </summary>
public interface IDeliveryChannelResolver
{
    /// <summary>
    /// Infers the delivery channel name from the user ID prefix.
    /// Returns null if no channel can be inferred.
    /// </summary>
    /// <param name="userId">The system user ID (e.g. "telegram:123456789").</param>
    /// <returns>The channel name (e.g. "telegram"), or null if unresolvable.</returns>
    string? ResolveFromUserId(string userId);
}
