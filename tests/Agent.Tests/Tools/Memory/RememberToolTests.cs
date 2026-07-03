using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

public class RememberToolTests
{
    private readonly Mock<IKnowledgeGraph> _graph = new();
    private readonly FakeTimeProvider _time = new();
    private readonly RememberTool _sut;

    public RememberToolTests()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        _graph.Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((KnowledgeRule r, CancellationToken _) => r);
        // Default: no pre-existing memories, so C3 supersede detection finds nothing.
        _graph.Setup(g => g.GetRulesAsync(
                  It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync((IReadOnlyList<KnowledgeRule>)[]);
        _sut = new RememberTool(_graph.Object, _time, NullLogger<RememberTool>.Instance);
    }

    private static ToolCall Call(string? content = "Bob prefers dark mode")
    {
        var args = new Dictionary<string, object?>();
        if (content is not null) args["content"] = content;
        return new ToolCall { Id = "tc-1", Name = "remember", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    private static KnowledgeRule ExistingMemory(string body, string owner = "user-1") =>
        MemoryNodes.Create(owner, body, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

    private void SetupExisting(params KnowledgeRule[] rules) =>
        _graph.Setup(g => g.GetRulesAsync(
                  It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(rules);

    private List<KnowledgeRule> CaptureUpserts()
    {
        var captured = new List<KnowledgeRule>();
        _graph.Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
              .Callback<KnowledgeRule, CancellationToken>((r, _) => captured.Add(r))
              .ReturnsAsync((KnowledgeRule r, CancellationToken _) => r);
        return captured;
    }

    [Fact]
    public async Task ExecuteAsync_UpsertsMemoryNode_ScopedToUser()
    {
        var result = await _sut.ExecuteAsync(Call(), Ctx("alice"));

        result.Success.Should().BeTrue();
        _graph.Verify(g => g.UpsertRuleAsync(
            It.Is<KnowledgeRule>(r =>
                r.OwnerId == "alice" &&
                r.SourceType == "memory" &&
                r.Type == "Memory" &&
                r.Body == "Bob prefers dark mode"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SameContent_ProducesSameId_Idempotent()
    {
        KnowledgeRule? first = null;
        KnowledgeRule? second = null;
        _graph.Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
              .Callback<KnowledgeRule, CancellationToken>((r, _) =>
              {
                  if (first is null) first = r; else second = r;
              })
              .ReturnsAsync((KnowledgeRule r, CancellationToken _) => r);

        await _sut.ExecuteAsync(Call("same fact"), Ctx("bob"));
        await _sut.ExecuteAsync(Call("same fact"), Ctx("bob"));

        first!.Id.Should().Be(second!.Id);
    }

    [Fact]
    public async Task ExecuteAsync_MissingContent_ReturnsFail_WithoutUpsert()
    {
        var result = await _sut.ExecuteAsync(Call(content: null), Ctx());

        result.Success.Should().BeFalse();
        _graph.Verify(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_BlankContent_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("   "), Ctx());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_GraphThrows_ReturnsFail()
    {
        _graph.Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("graph down"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("graph down");
    }

    [Fact]
    public async Task ExecuteAsync_HighOverlapWithExistingFact_SetsSupersedesEdge()
    {
        // {my,favorite,color,is,blue} vs {my,favorite,color,is,red}: 4/6 ≈ 0.667 > 0.6 threshold.
        var existing = ExistingMemory("my favorite color is blue");
        SetupExisting(existing);
        var upserts = CaptureUpserts();

        var result = await _sut.ExecuteAsync(Call("my favorite color is red"), Ctx());

        result.Success.Should().BeTrue();
        upserts.Should().ContainSingle();
        upserts[0].RelatesTo!.Supersedes.Should().ContainSingle().Which.Should().Be(existing.Id);
        result.Content.Should().Contain("Possibly supersedes");
    }

    [Fact]
    public async Task ExecuteAsync_DisjointFact_DoesNotSupersede()
    {
        SetupExisting(ExistingMemory("my favorite color is blue"));
        var upserts = CaptureUpserts();

        var result = await _sut.ExecuteAsync(Call("the deploy script lives under ops"), Ctx());

        result.Success.Should().BeTrue();
        upserts.Should().ContainSingle();
        upserts[0].RelatesTo.Should().BeNull();
        result.Content.Should().NotContain("supersedes");
    }

    [Fact]
    public async Task ExecuteAsync_SameContentAsExisting_DoesNotSupersedeItself()
    {
        // Remembering the identical fact again must not create a self-referential SUPERSEDES edge
        // (the existing node shares the new fact's deterministic id and is excluded).
        SetupExisting(ExistingMemory("my favorite color is blue"));
        var upserts = CaptureUpserts();

        var result = await _sut.ExecuteAsync(Call("my favorite color is blue"), Ctx());

        result.Success.Should().BeTrue();
        upserts.Should().ContainSingle();
        upserts[0].RelatesTo.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_SupersedeLookupThrows_StillRemembers()
    {
        // Best-effort: a failed lookup must never stop the fact being remembered, just without an edge.
        _graph.Setup(g => g.GetRulesAsync(
                  It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("graph read down"));
        var upserts = CaptureUpserts();

        var result = await _sut.ExecuteAsync(Call("my favorite color is red"), Ctx());

        result.Success.Should().BeTrue();
        upserts.Should().ContainSingle();
        upserts[0].RelatesTo.Should().BeNull();
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("remember");
    }

    [Fact]
    public async Task ExecuteAsync_ViewerRole_ReturnsInsufficientRoleFail()
    {
        var authorizer = new Mock<IRuleAuthorizer>();
        authorizer.Setup(a => a.CanMutateOwn()).Returns(false);
        var sut = new RememberTool(
            _graph.Object, _time, NullLogger<RememberTool>.Instance, authorizer: authorizer.Object);

        var result = await sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Insufficient role");
        _graph.Verify(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
