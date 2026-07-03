using Edda.Agent.Tests.TestUtilities;
using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

public class ManageUserdataToolTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly ManageUserdataTool _sut;

    public ManageUserdataToolTests()
    {
        _sut = new ManageUserdataTool(_fs, NullLogger<ManageUserdataTool>.Instance);
    }

    private static ToolCall Call(string action, string? content = null)
    {
        var args = new Dictionary<string, object?> { ["action"] = action };
        if (content is not null) args["content"] = content;
        return new ToolCall { Id = "tc-1", Name = "manage_userdata", Arguments = args };
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
    public async Task ExecuteAsync_Write_ThenRead_ReturnsSavedContent()
    {
        await _sut.ExecuteAsync(Call("write", "Name: Alice"), Ctx());
        var result = await _sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Be("Name: Alice");
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
    }

    [Fact]
    public async Task ExecuteAsync_UnknownAction_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("patch"), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("patch");
    }

    [Fact]
    public async Task ExecuteAsync_Delete_NoFile_ReturnsOkWithMessage()
    {
        var result = await _sut.ExecuteAsync(Call("delete"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("No userdata");
    }

    [Fact]
    public async Task ExecuteAsync_Delete_AfterWrite_RemovesFile()
    {
        await _sut.ExecuteAsync(Call("write", "x"), Ctx());
        var deleteResult = await _sut.ExecuteAsync(Call("delete"), Ctx());
        var readResult = await _sut.ExecuteAsync(Call("read"), Ctx());

        deleteResult.Success.Should().BeTrue();
        readResult.Success.Should().BeTrue();
        readResult.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Get_AliasForRead_ReturnsContent()
    {
        await _sut.ExecuteAsync(Call("write", "lang: de"), Ctx());
        var result = await _sut.ExecuteAsync(Call("get"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Be("lang: de");
    }

    [Fact]
    public async Task ExecuteAsync_Set_AliasForWrite_SavesContent()
    {
        await _sut.ExecuteAsync(Call("set", "theme: dark"), Ctx());
        var result = await _sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Be("theme: dark");
    }

    [Fact]
    public async Task ExecuteAsync_UserIdWithBackslash_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("read"), Ctx("user\\evil"));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_UserIdWithDotDot_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("read"), Ctx("user/../etc/passwd"));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("manage_userdata");
    }

    [Fact]
    public async Task ExecuteAsync_ViewerRoleWrite_ReturnsInsufficientRoleFail()
    {
        var authorizer = new Mock<IRuleAuthorizer>();
        authorizer.Setup(a => a.CanMutateOwn()).Returns(false);
        var sut = new ManageUserdataTool(_fs, NullLogger<ManageUserdataTool>.Instance, authorizer.Object);

        var result = await sut.ExecuteAsync(Call("set", "Name: Alice"), Ctx());

        result.Success.Should().BeFalse("the set alias maps to write and is role-gated");
        result.Error.Should().Contain("Insufficient role");
    }

    [Fact]
    public async Task ExecuteAsync_ViewerRoleRead_StillAllowed()
    {
        await _sut.ExecuteAsync(Call("write", "Name: Alice"), Ctx());
        var authorizer = new Mock<IRuleAuthorizer>();
        authorizer.Setup(a => a.CanMutateOwn()).Returns(false);
        var sut = new ManageUserdataTool(_fs, NullLogger<ManageUserdataTool>.Instance, authorizer.Object);

        var result = await sut.ExecuteAsync(Call("read"), Ctx());

        result.Success.Should().BeTrue("Viewers may read (matrix row 1)");
        result.Content.Should().Be("Name: Alice");
    }
}
