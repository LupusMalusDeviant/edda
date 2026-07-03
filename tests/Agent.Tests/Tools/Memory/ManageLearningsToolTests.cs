using Edda.Agent.Tests.TestUtilities;
using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

public class ManageLearningsToolTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeTimeProvider _time = new();
    private readonly Mock<IKnowledgeGraph> _knowledgeGraph = new();
    private readonly ManageLearningsTool _sut;

    public ManageLearningsToolTests()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 2, 25, 10, 30, 0, TimeSpan.Zero));
        _knowledgeGraph
            .Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeRule r, CancellationToken _) => r);
        _knowledgeGraph
            .Setup(g => g.DeleteRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sut = new ManageLearningsTool(_fs, _time, _knowledgeGraph.Object, NullLogger<ManageLearningsTool>.Instance);
    }

    private static ToolCall Call(string action, string? content = null)
    {
        var args = new Dictionary<string, object?> { ["action"] = action };
        if (content is not null) args["content"] = content;
        return new ToolCall { Id = "tc-1", Name = "manage_learnings", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    [Fact]
    public async Task ExecuteAsync_Read_NoFile_ReturnsEmpty()
    {
        var result = await _sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Append_AddsTimestampedEntry()
    {
        await _sut.ExecuteAsync(Call("append", "Always use async"), Ctx());
        var result = await _sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("[2026-02-25 10:30]");
        result.Content.Should().Contain("Always use async");
    }

    [Fact]
    public async Task ExecuteAsync_Append_MultipleEntries_AllPresent()
    {
        await _sut.ExecuteAsync(Call("append", "Entry one"), Ctx());
        await _sut.ExecuteAsync(Call("append", "Entry two"), Ctx());
        var result = await _sut.ExecuteAsync(Call("read"), Ctx());

        result.Content.Should().Contain("Entry one");
        result.Content.Should().Contain("Entry two");
    }

    [Fact]
    public async Task ExecuteAsync_Clear_RemovesFile()
    {
        await _sut.ExecuteAsync(Call("append", "some learning"), Ctx());
        await _sut.ExecuteAsync(Call("clear"), Ctx());
        var result = await _sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Append_MissingContent_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("append"), Ctx());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Append_WouldExceedSizeLimit_ReturnsFail()
    {
        // Fill up to near the limit first (102380 + ~43 for the new timestamped entry > 102400 limit)
        var nearLimitContent = new string('x', 102_380);
        _fs.AddFile("data/users/user-1/learnings.md", nearLimitContent);

        var result = await _sut.ExecuteAsync(Call("append", "extra content"), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("100 KB");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownAction_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("write"), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("write");
    }

    [Fact]
    public async Task ExecuteAsync_UserIdWithPathTraversal_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("read"), Ctx("../../etc"));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("manage_learnings");
    }

    [Fact]
    public async Task ExecuteAsync_Append_UpsertsMirrorNodeInAkg()
    {
        await _sut.ExecuteAsync(Call("append", "Use async always"), Ctx("user-42"));

        _knowledgeGraph.Verify(g => g.UpsertRuleAsync(
            It.Is<KnowledgeRule>(r =>
                r.Id == "learning-user-42" &&
                r.Domain == "learnings" &&
                r.Body.Contains("Use async always")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Clear_DeletesMirrorNodeFromAkg()
    {
        await _sut.ExecuteAsync(Call("append", "some learning"), Ctx("user-99"));
        await _sut.ExecuteAsync(Call("clear"), Ctx("user-99"));

        _knowledgeGraph.Verify(g => g.DeleteRuleAsync(
            "learning-user-99",
            "user-99",
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ViewerRoleAppend_ReturnsInsufficientRoleFail()
    {
        var authorizer = new Mock<IRuleAuthorizer>();
        authorizer.Setup(a => a.CanMutateOwn()).Returns(false);
        var sut = new ManageLearningsTool(
            _fs, _time, _knowledgeGraph.Object, NullLogger<ManageLearningsTool>.Instance, authorizer.Object);

        var result = await sut.ExecuteAsync(Call("append", "a learning"), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Insufficient role");
        _knowledgeGraph.Verify(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ViewerRoleRead_StillAllowed()
    {
        await _sut.ExecuteAsync(Call("append", "a learning"), Ctx());
        var authorizer = new Mock<IRuleAuthorizer>();
        authorizer.Setup(a => a.CanMutateOwn()).Returns(false);
        var sut = new ManageLearningsTool(
            _fs, _time, _knowledgeGraph.Object, NullLogger<ManageLearningsTool>.Instance, authorizer.Object);

        var result = await sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue("Viewers may read (matrix row 1)");
        result.Content.Should().Contain("a learning");
    }
}
