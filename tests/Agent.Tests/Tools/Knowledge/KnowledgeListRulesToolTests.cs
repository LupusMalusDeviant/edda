using Edda.Agent.Tools.Knowledge;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tools.Knowledge;

public class KnowledgeListRulesToolTests
{
    private readonly Mock<IKnowledgeGraph> _kg = new();
    private readonly KnowledgeListRulesTool _sut;

    public KnowledgeListRulesToolTests()
    {
        _sut = new KnowledgeListRulesTool(_kg.Object, NullLogger<KnowledgeListRulesTool>.Instance);
    }

    private static ToolCall Call(string? domain = null, string? type = null, string? tag = null)
    {
        var args = new Dictionary<string, object?>();
        if (domain is not null) args["domain"] = domain;
        if (type is not null) args["type"] = type;
        if (tag is not null) args["tag"] = tag;
        return new ToolCall { Id = "tc-1", Name = "list_memory", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    private static KnowledgeRule MakeRule(string id, string domain = "csharp") =>
        new()
        {
            Id = id,
            Type = "convention",
            Domain = domain,
            Priority = RulePriority.Medium,
            Body = $"Rule body for {id}",
            Tags = ["tag-a"]
        };

    [Fact]
    public async Task ExecuteAsync_NoFilters_ReturnsAllRules()
    {
        _kg.Setup(k => k.GetRulesAsync(null, null, null, "user-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync([MakeRule("rule-1"), MakeRule("rule-2")]);

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("rule-1");
        result.Content.Should().Contain("rule-2");
    }

    [Fact]
    public async Task ExecuteAsync_DomainFilter_PassesFilterToKg()
    {
        _kg.Setup(k => k.GetRulesAsync("csharp", null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync([MakeRule("use-async")]);

        var result = await _sut.ExecuteAsync(Call(domain: "csharp"), Ctx());

        result.Success.Should().BeTrue();
        _kg.Verify(k => k.GetRulesAsync("csharp", null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyRules_ReturnsEmptyArray()
    {
        _kg.Setup(k => k.GetRulesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync([]);

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Be("[]");
    }

    [Fact]
    public async Task ExecuteAsync_KgThrows_ReturnsFail()
    {
        _kg.Setup(k => k.GetRulesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("graph error"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("graph error");
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("list_memory");
    }
}
