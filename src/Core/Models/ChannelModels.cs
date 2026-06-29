namespace Edda.Core.Models;

/// <summary>
/// Context provided by an incoming agent channel when building an AgentRequest.
/// Each channel implementation provides its own IChannelContext.
/// </summary>
public interface IChannelContext
{
    /// <summary>Channel-specific user identifier, mapped to the system user ID.</summary>
    string? UserId { get; }

    /// <summary>Display name of the user in the originating channel.</summary>
    string? Username { get; }

    /// <summary>Channel-specific metadata (e.g. telegram_chat_id).</summary>
    IReadOnlyDictionary<string, string> Metadata { get; }
}
