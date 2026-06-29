using Edda.AKG.Ingestion.Connectors;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Connectors;

/// <summary>Unit tests for <see cref="ConnectorRegistry"/> descriptor exposure and routing.</summary>
public sealed class ConnectorRegistryTests
{
    private static Mock<IKnowledgeConnector> FakeConnector(string typeId, IngestionResult? result = null)
    {
        var connector = new Mock<IKnowledgeConnector>();
        connector.SetupGet(c => c.TypeId).Returns(typeId);
        connector.Setup(c => c.Describe()).Returns(new ConnectorDescriptor { TypeId = typeId, DisplayName = typeId });
        connector.Setup(c => c.RunAsync(It.IsAny<ConnectorInstanceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result ?? new IngestionResult { Imported = 1 });
        return connector;
    }

    private static ConnectorInstanceConfig Instance(string typeId) =>
        new() { Id = "s1", TypeId = typeId, Name = "Source" };

    [Fact]
    public void Describe_ReturnsAllConnectorDescriptors()
    {
        var registry = new ConnectorRegistry([FakeConnector("git").Object, FakeConnector("custom-http").Object]);

        registry.Describe().Select(d => d.TypeId).Should().BeEquivalentTo(["git", "custom-http"]);
    }

    [Fact]
    public async Task RunAsync_RoutesToMatchingConnector()
    {
        var registry = new ConnectorRegistry([FakeConnector("git", new IngestionResult { Imported = 7 }).Object]);

        var result = await registry.RunAsync(Instance("git"));

        result.Imported.Should().Be(7);
    }

    [Fact]
    public async Task RunAsync_UnknownType_ReturnsFailedResult()
    {
        var registry = new ConnectorRegistry([FakeConnector("git").Object]);

        var result = await registry.RunAsync(Instance("does-not-exist"));

        result.Failed.Should().Be(1);
        result.Errors.Should().ContainSingle();
    }
}
