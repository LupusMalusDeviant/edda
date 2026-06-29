namespace Edda.Core.Abstractions;

/// <summary>
/// Simple in-process event bus for publishing domain events.
/// Used by <see cref="IAgentRuntime"/> to publish <c>AgentResponseEvent</c> after each turn.
/// Implementations can fan out to SSE streams, background workers, or monitoring systems.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to all registered subscribers.
    /// </summary>
    /// <typeparam name="T">Event type. Must be a reference type.</typeparam>
    /// <param name="event">The event payload to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
        where T : class;
}
