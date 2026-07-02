using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

/// <summary>
/// Unit tests for <see cref="ConsolidateTool"/>: a thin wrapper that delegates to
/// <see cref="IMemoryConsolidator"/> using the caller's scoped user id. The consolidation logic itself is
/// covered by <c>MemoryConsolidatorTests</c>.
/// </summary>
public class ConsolidateToolTests
{
    private readonly Mock<IMemoryConsolidator> _consolidator = new();
    private readonly ConsolidateTool _sut;

    public ConsolidateToolTests()
        => _sut = new ConsolidateTool(_consolidator.Object, NullLogger<ConsolidateTool>.Instance);

    private static ToolCall Call() =>
        new() { Id = "tc-1", Name = "consolidate_memory", Arguments = new Dictionary<string, object?>() };

    private static ToolExecutionContext Ctx(string? userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    [Fact]
    public async Task ExecuteAsync_DelegatesToConsolidator_WithContextUserId()
    {
        _consolidator.Setup(c => c.ConsolidateUserAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryConsolidationResult(1, 2, 3));

        var result = await _sut.ExecuteAsync(Call(), Ctx("user-1"));

        result.Success.Should().BeTrue();
        _consolidator.Verify(c => c.ConsolidateUserAsync("user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NullUserId_UsesAnonymous()
    {
        _consolidator.Setup(c => c.ConsolidateUserAsync("anonymous", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryConsolidationResult(1, 0, 0));

        await _sut.ExecuteAsync(Call(), Ctx(userId: null));

        _consolidator.Verify(c => c.ConsolidateUserAsync("anonymous", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ConsolidatorThrows_ReturnsFail()
    {
        _consolidator.Setup(c => c.ConsolidateUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("graph down"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ListsMergedAwayBodies()
    {
        _consolidator.Setup(c => c.ConsolidateUserAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryConsolidationResult(1, 0, 0, 1) { MergedAwayBodies = ["old fact"] });

        var result = await _sut.ExecuteAsync(Call(), Ctx("user-1"));

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("near-duplicate(s)");
        result.Content.Should().Contain("Merged away: old fact");
    }

    [Fact]
    public void Definition_HasCorrectName()
        => _sut.Definition.Name.Should().Be("consolidate_memory");
}
