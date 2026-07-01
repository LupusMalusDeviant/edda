using Edda.Agent.Tools.Memory;

namespace Edda.Agent.Tests.Tools.Memory;

public class MemoryNodesTests
{
    [Fact]
    public void NodeId_SameUserAndContent_IsDeterministic()
    {
        MemoryNodes.NodeId("user-1", "Bob likes tea")
            .Should().Be(MemoryNodes.NodeId("user-1", "Bob likes tea"));
    }

    [Fact]
    public void NodeId_TrimsContent_SoWhitespaceVariantsMatch()
    {
        MemoryNodes.NodeId("user-1", "Bob likes tea")
            .Should().Be(MemoryNodes.NodeId("user-1", "  Bob likes tea  "));
    }

    [Fact]
    public void NodeId_DifferentContent_Differs()
    {
        MemoryNodes.NodeId("user-1", "a").Should().NotBe(MemoryNodes.NodeId("user-1", "b"));
    }

    [Fact]
    public void NodeId_DifferentUser_Differs()
    {
        MemoryNodes.NodeId("user-1", "same").Should().NotBe(MemoryNodes.NodeId("user-2", "same"));
    }

    [Fact]
    public void NodeId_StartsWithMemoryAndUser()
    {
        MemoryNodes.NodeId("alice", "x").Should().StartWith("memory-alice-");
    }

    [Fact]
    public void Create_SetsMemoryScopingFields_AndTrimsBody()
    {
        var when = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        var rule = MemoryNodes.Create("user-7", "  a fact  ", when);

        rule.OwnerId.Should().Be("user-7");
        rule.SourceType.Should().Be("memory");
        rule.Type.Should().Be("Memory");
        rule.Domain.Should().Be("memory");
        rule.Body.Should().Be("a fact");
        rule.Id.Should().Be(MemoryNodes.NodeId("user-7", "a fact"));
        rule.Created.Should().Be(new DateOnly(2026, 3, 1));
        rule.Tags.Should().Contain("memory").And.Contain("user-7");
    }
}
