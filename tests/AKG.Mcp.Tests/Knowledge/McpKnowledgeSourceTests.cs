using Edda.AKG.Mcp.Client;
using Edda.AKG.Mcp.Knowledge;
using Edda.AKG.Mcp.Models;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Mcp.Tests.Knowledge;

/// <summary>Unit tests for <see cref="McpKnowledgeSource"/>.</summary>
public sealed class McpKnowledgeSourceTests
{
    [Fact]
    public void SourceKind_IsMcp()
        => new McpKnowledgeSource(Mock.Of<IExternalMcpClientFactory>()).SourceKind.Should().Be("mcp");

    [Fact]
    public void BuildItems_CreatesSourceNode_AndLinkedContentItems()
    {
        var items = McpKnowledgeSource.BuildItems("My Source", "ops", ["# Doc A\n\nbody", "Doc B"]);

        items.Select(i => i.Id).Should().Contain(new[] { "mcp:my-source", "mcp:my-source:0", "mcp:my-source:1" });
        items.Single(i => i.Id == "mcp:my-source:0").Title.Should().Be("Doc A");
        items.Single(i => i.Id == "mcp:my-source:0").NativeLinks.Should().Contain(l => l.TargetRef == "mcp:my-source");
        items.Should().OnlyContain(i => i.Domain == "ops");
    }

    [Fact]
    public void ParseArguments_ValidObject_Parsed()
    {
        var args = McpKnowledgeSource.ParseArguments("{\"query\":\"x\",\"limit\":5}");

        args.Should().ContainKey("query").And.ContainKey("limit");
    }

    [Fact]
    public void ParseArguments_Blank_ReturnsEmpty()
        => McpKnowledgeSource.ParseArguments("  ").Should().BeEmpty();

    [Fact]
    public async Task FetchAsync_CallsTool_AndMapsContent()
    {
        var client = new Mock<IExternalMcpClient>();
        client.Setup(c => c.CallToolAsync("search", It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpToolResult { Content = [new McpTextContent("Doc 1"), new McpTextContent("Doc 2")] });
        var factory = new Mock<IExternalMcpClientFactory>();
        factory.Setup(f => f.Create("https://mcp.example", "tok")).Returns(client.Object);

        var source = new McpKnowledgeSource(factory.Object);
        var config = new IngestionSourceConfig
        {
            Token = "tok",
            Settings = new Dictionary<string, string>
            {
                [McpKnowledgeSource.ServerUrlKey] = "https://mcp.example",
                [McpKnowledgeSource.ToolNameKey] = "search",
                [McpKnowledgeSource.LabelKey] = "docs",
            },
        };

        var items = new List<IngestionItem>();
        await foreach (var item in source.FetchAsync(config))
            items.Add(item);

        items.Select(i => i.Id).Should().Contain(new[] { "mcp:docs", "mcp:docs:0", "mcp:docs:1" });
        factory.Verify(f => f.Create("https://mcp.example", "tok"), Times.Once);
    }

    [Fact]
    public async Task FetchAsync_MissingServerOrTool_YieldsNothing()
    {
        var source = new McpKnowledgeSource(Mock.Of<IExternalMcpClientFactory>());
        var config = new IngestionSourceConfig
        {
            Settings = new Dictionary<string, string> { [McpKnowledgeSource.ToolNameKey] = "x" },
        };

        var items = new List<IngestionItem>();
        await foreach (var item in source.FetchAsync(config))
            items.Add(item);

        items.Should().BeEmpty();
    }
}
