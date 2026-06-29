using Edda.AKG.Activity;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Activity;

/// <summary>Unit tests for <see cref="ActivityTracker"/>: state tracking, updates, and change notification.</summary>
public sealed class ActivityTrackerTests
{
    private readonly ActivityTracker _sut = new(TimeProvider.System);

    [Fact]
    public void Snapshots_NothingReported_IsEmpty()
        => _sut.Snapshots.Should().BeEmpty();

    [Fact]
    public void Report_NewActivity_AppearsInSnapshotsWithStateAndDetail()
    {
        _sut.Report(ActivityKind.Import, ActivityState.Running, "Example Git");

        var snapshot = _sut.Snapshots.Should().ContainSingle().Subject;
        snapshot.Kind.Should().Be(ActivityKind.Import);
        snapshot.State.Should().Be(ActivityState.Running);
        snapshot.Detail.Should().Be("Example Git");
    }

    [Fact]
    public void Report_RaisesChangedEvent()
    {
        var raised = 0;
        _sut.Changed += () => raised++;

        _sut.Report(ActivityKind.Embedding, ActivityState.Running);

        raised.Should().Be(1);
    }

    [Fact]
    public void Report_SameKindTwice_UpdatesInPlace()
    {
        _sut.Report(ActivityKind.Embedding, ActivityState.Running, "0/10");
        _sut.Report(ActivityKind.Embedding, ActivityState.Succeeded, "10 Regeln");

        var snapshot = _sut.Snapshots.Should().ContainSingle().Subject;
        snapshot.State.Should().Be(ActivityState.Succeeded);
        snapshot.Detail.Should().Be("10 Regeln");
    }

    [Fact]
    public void Report_MultipleKinds_AllTrackedIndependently()
    {
        _sut.Report(ActivityKind.Import, ActivityState.Succeeded);
        _sut.Report(ActivityKind.Chunking, ActivityState.Running);
        _sut.Report(ActivityKind.Embedding, ActivityState.Failed);

        _sut.Snapshots.Select(s => s.Kind)
            .Should().BeEquivalentTo([ActivityKind.Import, ActivityKind.Chunking, ActivityKind.Embedding]);
    }

    [Fact]
    public void Report_RunningWithCancelCallback_MarksSnapshotCancellable()
    {
        _sut.Report(ActivityKind.Embedding, ActivityState.Running, onCancel: () => { });

        _sut.Snapshots.Single().CanCancel.Should().BeTrue();
    }

    [Fact]
    public void Cancel_InvokesRegisteredCallback()
    {
        var cancelled = false;
        _sut.Report(ActivityKind.Embedding, ActivityState.Running, onCancel: () => cancelled = true);

        _sut.Cancel(ActivityKind.Embedding);

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void Report_TerminalState_ClearsCancellation()
    {
        var cancelled = false;
        _sut.Report(ActivityKind.Import, ActivityState.Running, onCancel: () => cancelled = true);
        _sut.Report(ActivityKind.Import, ActivityState.Succeeded);

        _sut.Snapshots.Single().CanCancel.Should().BeFalse();
        _sut.Cancel(ActivityKind.Import);
        cancelled.Should().BeFalse();
    }

    [Fact]
    public void Report_RunningProgressUpdate_KeepsCancelCallback()
    {
        var cancelled = false;
        _sut.Report(ActivityKind.Embedding, ActivityState.Running, "0/10", onCancel: () => cancelled = true);
        _sut.Report(ActivityKind.Embedding, ActivityState.Running, "5/10");   // no callback re-supplied

        _sut.Snapshots.Single().CanCancel.Should().BeTrue();
        _sut.Cancel(ActivityKind.Embedding);
        cancelled.Should().BeTrue();
    }

    [Fact]
    public void Cancel_NoActivity_DoesNotThrow()
    {
        var act = () => _sut.Cancel(ActivityKind.Embedding);

        act.Should().NotThrow();
    }
}
