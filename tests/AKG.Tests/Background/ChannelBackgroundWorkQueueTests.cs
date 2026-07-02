using Edda.AKG.Background;

namespace Edda.AKG.Tests.Background;

public sealed class ChannelBackgroundWorkQueueTests
{
    [Fact]
    public async Task EnqueueThenDequeue_ReturnsSameWorkAndDescription()
    {
        var queue = new ChannelBackgroundWorkQueue();
        Func<CancellationToken, Task> work = _ => Task.CompletedTask;

        queue.Enqueue(work, "the-label");
        var item = await queue.DequeueAsync(CancellationToken.None);

        item.Work.Should().BeSameAs(work);
        item.Description.Should().Be("the-label");
    }

    [Fact]
    public async Task DequeueAsync_MultipleItems_ReturnsInFifoOrder()
    {
        var queue = new ChannelBackgroundWorkQueue();
        queue.Enqueue(_ => Task.CompletedTask, "first");
        queue.Enqueue(_ => Task.CompletedTask, "second");

        (await queue.DequeueAsync(CancellationToken.None)).Description.Should().Be("first");
        (await queue.DequeueAsync(CancellationToken.None)).Description.Should().Be("second");
    }

    [Fact]
    public async Task DequeueAsync_EmptyQueue_CompletesOnlyOnceEnqueued()
    {
        var queue = new ChannelBackgroundWorkQueue();

        var pending = queue.DequeueAsync(CancellationToken.None).AsTask();
        pending.IsCompleted.Should().BeFalse();

        queue.Enqueue(_ => Task.CompletedTask, "arrived");
        var item = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        item.Description.Should().Be("arrived");
    }

    [Fact]
    public async Task DequeueAsync_TokenCancelled_ThrowsOperationCanceled()
    {
        var queue = new ChannelBackgroundWorkQueue();
        using var cts = new CancellationTokenSource();

        var pending = queue.DequeueAsync(cts.Token).AsTask();
        await cts.CancelAsync();

        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Enqueue_NullWork_ThrowsArgumentNullException()
    {
        var queue = new ChannelBackgroundWorkQueue();

        var act = () => queue.Enqueue(null!, "x");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Enqueue_NullDescription_StoredAsEmptyString()
    {
        var queue = new ChannelBackgroundWorkQueue();

        queue.Enqueue(_ => Task.CompletedTask, null!);
        var item = await queue.DequeueAsync(CancellationToken.None);

        item.Description.Should().BeEmpty();
    }
}
