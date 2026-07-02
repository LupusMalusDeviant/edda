using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Background;

/// <summary>
/// Hosted consumer that drains the <see cref="IBackgroundWorkQueue"/> on a single long-running loop.
/// Each work item receives the service's stopping token, so background jobs are cancelled when the
/// host shuts down; a failing item is logged and isolated so it never tears down the loop or the
/// process.
/// </summary>
internal sealed class BackgroundWorkQueueConsumer : BackgroundService
{
    private readonly IBackgroundWorkQueue _queue;
    private readonly ILogger<BackgroundWorkQueueConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundWorkQueueConsumer"/> class.
    /// </summary>
    /// <param name="queue">The queue to drain.</param>
    /// <param name="logger">Logger for failure diagnostics.</param>
    public BackgroundWorkQueueConsumer(
        IBackgroundWorkQueue queue,
        ILogger<BackgroundWorkQueueConsumer> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            BackgroundWorkItem item;
            try
            {
                item = await _queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down while we were waiting for work — exit the loop quietly.
                break;
            }

            try
            {
                await item.Work(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The item observed the shutdown token and stopped — expected, not a failure.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Background work item '{Description}' failed | {Component}", item.Description, "AKG");
            }
        }
    }
}
