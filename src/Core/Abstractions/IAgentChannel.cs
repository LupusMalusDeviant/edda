using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Abstracts incoming agent channels (Telegram, REST, CLI, ...).
/// Add a new channel by implementing this interface plus IHostedService and registering in DI.
/// Each channel is responsible for identity mapping and metadata injection.
/// </summary>
public interface IAgentChannel
{
    /// <summary>
    /// Unique channel identifier.
    /// Known values: "telegram", "rest", "cli".
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// Builds a typed AgentRequest from channel-specific raw data.
    /// Responsible for identity mapping and metadata injection into ChannelMetadata.
    /// </summary>
    /// <param name="userMessage">The raw user message text.</param>
    /// <param name="conversationId">The conversation session identifier.</param>
    /// <param name="channelContext">Channel-specific context including user identity and metadata.</param>
    /// <returns>A fully populated AgentRequest ready for IAgentRuntime.ProcessAsync().</returns>
    AgentRequest BuildRequest(
        string userMessage,
        string conversationId,
        IChannelContext channelContext);
}
