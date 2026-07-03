using Edda.Agent.Tests.TestUtilities;
using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

public class ManageMemoryToolTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly ManageMemoryTool _sut;

    public ManageMemoryToolTests()
    {
        _sut = new ManageMemoryTool(_fs, NullLogger<ManageMemoryTool>.Instance);
    }

    private static ToolCall Call(string action, string? content = null)
    {
        var args = new Dictionary<string, object?> { ["action"] = action };
        if (content is not null) args["content"] = content;
        return new ToolCall { Id = "tc-1", Name = "manage_memory", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    [Fact]
    public async Task ExecuteAsync_Read_EmptyFile_ReturnsEmptyString()
    {
        var result = await _sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Write_ThenRead_ReturnsSavedContent()
    {
        await _sut.ExecuteAsync(Call("write", "Hello memory"), Ctx());
        var result = await _sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Be("Hello memory");
    }

    [Fact]
    public async Task ExecuteAsync_Clear_RemovesFile()
    {
        await _sut.ExecuteAsync(Call("write", "some data"), Ctx());
        await _sut.ExecuteAsync(Call("clear"), Ctx());
        var result = await _sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Write_ExceedsSizeLimit_ReturnsFail()
    {
        var bigContent = new string('x', 102_401); // one byte over the 100 KB limit

        var result = await _sut.ExecuteAsync(Call("write", bigContent), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("100 KB");
    }

    [Fact]
    public async Task ExecuteAsync_Write_MissingContent_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("write"), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownAction_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("purge"), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("purge");
    }

    [Fact]
    public async Task ExecuteAsync_UserIdWithPathTraversal_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("read"), Ctx("../evil"));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("userId");
    }

    [Fact]
    public async Task ExecuteAsync_NullUserId_UsesAnonymous()
    {
        var result = await _sut.ExecuteAsync(Call("read"), new ToolExecutionContext { ConversationId = "c", UserId = null });

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_TwoUsers_HaveIsolatedMemory()
    {
        await _sut.ExecuteAsync(Call("write", "Alice data"), Ctx("alice"));
        await _sut.ExecuteAsync(Call("write", "Bob data"), Ctx("bob"));

        var aliceResult = await _sut.ExecuteAsync(Call("read"), Ctx("alice"));
        var bobResult = await _sut.ExecuteAsync(Call("read"), Ctx("bob"));

        aliceResult.Content.Should().Be("Alice data");
        bobResult.Content.Should().Be("Bob data");
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("manage_memory");
    }

    [Fact]
    public async Task ExecuteAsync_ViewerRoleWrite_ReturnsInsufficientRoleFail()
    {
        var authorizer = new Mock<IRuleAuthorizer>();
        authorizer.Setup(a => a.CanMutateOwn()).Returns(false);
        var sut = new ManageMemoryTool(_fs, NullLogger<ManageMemoryTool>.Instance, authorizer.Object);

        var result = await sut.ExecuteAsync(Call("write", "data"), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Insufficient role");
    }

    [Fact]
    public async Task ExecuteAsync_ViewerRoleRead_StillAllowed()
    {
        await _sut.ExecuteAsync(Call("write", "existing"), Ctx());
        var authorizer = new Mock<IRuleAuthorizer>();
        authorizer.Setup(a => a.CanMutateOwn()).Returns(false);
        var sut = new ManageMemoryTool(_fs, NullLogger<ManageMemoryTool>.Instance, authorizer.Object);

        var result = await sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue("Viewers may read (matrix row 1)");
        result.Content.Should().Be("existing");
    }
}
