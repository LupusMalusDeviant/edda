using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Main entry point for the 10-phase agent processing pipeline.
/// One instance is used per channel (REST, Telegram, CLI).
/// </summary>
public interface IAgentRuntime
{
    /// <summary>
    /// Executes all 10 pipeline phases non-streaming and returns the complete response.
    /// </summary>
    /// <param name="request">The incoming agent request built by the originating channel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The completed agent response with content and metadata.</returns>
    /// <exception cref="Exceptions.AgentException">Thrown on unrecoverable pipeline failures.</exception>
    Task<AgentResponse> ProcessAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the pipeline in streaming mode, yielding events incrementally.
    /// TDK validation is performed after the stream is fully consumed, not during.
    /// </summary>
    /// <param name="request">The incoming agent request built by the originating channel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async sequence of stream events.</returns>
    IAsyncEnumerable<StreamEvent> ProcessStreamingAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default);
}
