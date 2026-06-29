using Edda.Core.Models;

namespace Edda.Core.Tests.Models;

/// <summary>
/// Unit tests for <see cref="StreamEvent"/> factory methods and <see cref="StreamEventType"/> enum.
/// </summary>
public sealed class StreamEventTests
{
    [Fact]
    public void Text_CreatesTextDeltaEvent()
    {
        var evt = StreamEvent.TextDelta("hello");
        evt.Type.Should().Be(StreamEventType.TextDelta);
        evt.Text.Should().Be("hello");
    }

    [Fact]
    public void ToolExecuting_CreatesToolExecutingEvent()
    {
        var evt = StreamEvent.ToolExecuting("web_search");
        evt.Type.Should().Be(StreamEventType.ToolExecuting);
        evt.ToolName.Should().Be("web_search");
    }

    [Fact]
    public void ToolComplete_SuccessTrue_CreatesSuccessfulEvent()
    {
        var evt = StreamEvent.ToolComplete("web_search", success: true);
        evt.Type.Should().Be(StreamEventType.ToolComplete);
        evt.ToolName.Should().Be("web_search");
        evt.ToolResult!.Success.Should().BeTrue();
        evt.ToolResult.Name.Should().Be("web_search");
    }

    [Fact]
    public void ToolComplete_SuccessFalse_CreatesFailedEvent()
    {
        var evt = StreamEvent.ToolComplete("bad_tool", success: false);
        evt.Type.Should().Be(StreamEventType.ToolComplete);
        evt.ToolResult!.Success.Should().BeFalse();
    }

    [Fact]
    public void TurnComplete_CreatesTurnCompleteEvent()
    {
        var evt = StreamEvent.TurnComplete();
        evt.Type.Should().Be(StreamEventType.TurnComplete);
        evt.Text.Should().BeNull();
        evt.ToolName.Should().BeNull();
    }

    [Fact]
    public void System_CreatesSystemNoteEvent()
    {
        var evt = StreamEvent.System("LoopGuard warning");
        evt.Type.Should().Be(StreamEventType.SystemNote);
        evt.Text.Should().Be("LoopGuard warning");
    }

    [Fact]
    public void StreamEventType_SystemNote_EnumValueExists()
    {
        var defined = Enum.IsDefined(typeof(StreamEventType), StreamEventType.SystemNote);
        defined.Should().BeTrue();
    }
}
