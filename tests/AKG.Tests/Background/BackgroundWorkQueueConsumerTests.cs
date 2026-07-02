using Edda.AKG.Background;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edda.AKG.Tests.Background;

public sealed class BackgroundWorkQueueConsumerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ExecuteAsync_RunsEnqueuedWork()
    {
        var queue = new ChannelBackgroundWorkQueue();
        var consumer = new BackgroundWorkQueueConsumer(queue, NullLogger<BackgroundWorkQueueConsumer>.Instance);
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await consumer.StartAsync(CancellationToken.None);
        queue.Enqueue(_ => { ran.SetResult(); return Task.CompletedTask; }, "work");

        await ran.Task.WaitAsync(Timeout);
        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_ItemThrows_IsolatesFailureAndRunsNextItem()
    {
        var queue = new ChannelBackgroundWorkQueue();
        var consumer = new BackgroundWorkQueueConsumer(queue, NullLogger<BackgroundWorkQueueConsumer>.Instance);
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await consumer.StartAsync(CancellationToken.None);
        // A throwing item must not tear down the consumer loop — the next item still runs.
        queue.Enqueue(_ => throw new InvalidOperationException("boom"), "failing");
        queue.Enqueue(_ => { secondRan.SetResult(); return Task.CompletedTask; }, "next");

        await secondRan.Task.WaitAsync(Timeout);
        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_PassesStoppingTokenToWork_CancelledOnStop()
    {
        var queue = new ChannelBackgroundWorkQueue();
        var consumer = new BackgroundWorkQueueConsumer(queue, NullLogger<BackgroundWorkQueueConsumer>.Instance);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;

        await consumer.StartAsync(CancellationToken.None);
        queue.Enqueue(ct => { observed = ct; captured.SetResult(); return Task.CompletedTask; }, "capture");

        await captured.Task.WaitAsync(Timeout);
        // While the host is running, the token handed to work is live (not cancelled).
        observed.IsCancellationRequested.Should().BeFalse();

        await consumer.StopAsync(CancellationToken.None);
        // Stopping the host cancels the same token — background jobs observe shutdown.
        observed.IsCancellationRequested.Should().BeTrue();
    }
}
