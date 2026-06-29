using Edda.Agent.Knowledge;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Knowledge;

public sealed class ToolKnowledgeServiceTests
{
    private readonly Mock<IKnowledgeGraph> _graphMock = new();
    private readonly ToolKnowledgeService _sut;

    public ToolKnowledgeServiceTests()
    {
        _graphMock
            .Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeRule r, CancellationToken _) => r);

        _sut = new ToolKnowledgeService(
            _graphMock.Object,
            NullLogger<ToolKnowledgeService>.Instance);
    }

    [Fact]
    public async Task UpsertCustomToolRuleAsync_CreatesCorrectRule()
    {
        await _sut.UpsertCustomToolRuleAsync("weather-fetcher", "Fetches weather data", ["api", "weather"], "user-1");

        _graphMock.Verify(g => g.UpsertRuleAsync(
            It.Is<KnowledgeRule>(r =>
                r.Id == "custom-tool-weather-fetcher" &&
                r.Domain == "custom-tools" &&
                r.Type == "Guideline" &&
                r.Priority == RulePriority.Medium &&
                r.OwnerId == "user-1" &&
                r.Tags.Contains("tool") &&
                r.Tags.Contains("custom-tool") &&
                r.Tags.Contains("weather-fetcher") &&
                r.Tags.Contains("api") &&
                r.Tags.Contains("weather") &&
                r.Body.Contains("weather-fetcher") &&
                r.Body.Contains("Fetches weather data") &&
                r.WhenRelevant != null &&
                r.WhenRelevant.DetectedConcepts.Count > 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertCustomToolRuleAsync_SanitizesName()
    {
        await _sut.UpsertCustomToolRuleAsync("My Cool Tool!", "Does things", [], "user-1");

        _graphMock.Verify(g => g.UpsertRuleAsync(
            It.Is<KnowledgeRule>(r => r.Id == "custom-tool-my-cool-tool"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertCustomToolRuleAsync_DeduplicatesTags()
    {
        await _sut.UpsertCustomToolRuleAsync("my-tool", "desc", ["tool", "custom-tool", "extra"], "user-1");

        _graphMock.Verify(g => g.UpsertRuleAsync(
            It.Is<KnowledgeRule>(r =>
                r.Tags.Count(t => t == "tool") == 1 &&
                r.Tags.Count(t => t == "custom-tool") == 1 &&
                r.Tags.Contains("extra")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCustomToolRuleAsync_CallsDeleteRule()
    {
        await _sut.DeleteCustomToolRuleAsync("weather-fetcher", "user-1");

        _graphMock.Verify(g => g.DeleteRuleAsync(
            "custom-tool-weather-fetcher", "user-1", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCustomToolRuleAsync_NonExistent_DoesNotThrow()
    {
        _graphMock
            .Setup(g => g.DeleteRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Rule not found"));

        var act = () => _sut.DeleteCustomToolRuleAsync("nonexistent", "user-1");

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("hello world", "hello-world")]
    [InlineData("my_cool_tool", "my-cool-tool")]
    [InlineData("Tool.With.Dots", "tool-with-dots")]
    [InlineData("  spaced  out  ", "spaced-out")]
    [InlineData("UPPER-CASE", "upper-case")]
    [InlineData("special!@#chars", "specialchars")]
    public void ToKebabCase_ConvertsCorrectly(string input, string expected)
    {
        ToolKnowledgeService.ToKebabCase(input).Should().Be(expected);
    }

    [Fact]
    public void ExtractConcepts_ExtractsFromNameAndDescription()
    {
        var concepts = ToolKnowledgeService.ExtractConcepts("weather-api", "Fetches current weather data from OpenWeatherMap");

        concepts.Should().Contain("weather");
        concepts.Should().Contain("custom-tool");
        // "api" is only 3 chars, so it won't be in name-derived concepts (min 3 chars)
        // But description words >= 4 chars should be included
        concepts.Should().Contain("fetches");
    }
}
