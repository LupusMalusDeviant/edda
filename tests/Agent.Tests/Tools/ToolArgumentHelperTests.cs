using System.Text.Json;
using Edda.Agent.Tools;

namespace Edda.Agent.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="ToolArgumentHelper"/>.
/// Covers <c>GetStringArray</c>, <c>GetBool</c>, and <c>GetObjectArray</c>.
/// </summary>
public class ToolArgumentHelperTests
{
    // ── GetStringArray ─────────────────────────────────────────────────────────

    [Fact]
    public void GetStringArray_JsonElementArray_ReturnsStrings()
    {
        // Arrange — simulate what System.Text.Json produces when the model sends
        // {"subtasks":["Alpha","Beta","Gamma"]}
        var doc = JsonDocument.Parse("""{"subtasks":["Alpha","Beta","Gamma"]}""");
        var args = new Dictionary<string, object?>
        {
            ["subtasks"] = doc.RootElement.GetProperty("subtasks")
        };

        // Act
        var result = ToolArgumentHelper.GetStringArray(args, "subtasks");

        // Assert
        result.Should().Equal("Alpha", "Beta", "Gamma");
    }

    // ── GetBool ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetBool_JsonElementTrue_ReturnsTrue()
    {
        var doc = JsonDocument.Parse("""{"parallel":true}""");
        var args = new Dictionary<string, object?> { ["parallel"] = doc.RootElement.GetProperty("parallel") };

        ToolArgumentHelper.GetBool(args, "parallel").Should().BeTrue();
    }

    [Fact]
    public void GetBool_JsonElementFalse_ReturnsFalse()
    {
        var doc = JsonDocument.Parse("""{"parallel":false}""");
        var args = new Dictionary<string, object?> { ["parallel"] = doc.RootElement.GetProperty("parallel") };

        ToolArgumentHelper.GetBool(args, "parallel", defaultValue: true).Should().BeFalse();
    }

    [Fact]
    public void GetBool_MissingKey_ReturnsDefaultValue()
    {
        var args = new Dictionary<string, object?>();

        ToolArgumentHelper.GetBool(args, "parallel", defaultValue: true).Should().BeTrue();
    }

    // ── GetStringArray ─────────────────────────────────────────────────────────

    [Fact]
    public void GetStringArray_MissingKey_ReturnsEmptyArray()
    {
        var args = new Dictionary<string, object?>
        {
            ["other"] = "value"
        };

        var result = ToolArgumentHelper.GetStringArray(args, "subtasks");

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetStringArray_NonArrayValue_ReturnsEmptyArray()
    {
        // Arrange — value is a JSON number, not an array
        var doc = JsonDocument.Parse("""{"count":42}""");
        var args = new Dictionary<string, object?>
        {
            ["count"] = doc.RootElement.GetProperty("count")
        };

        var result = ToolArgumentHelper.GetStringArray(args, "count");

        result.Should().BeEmpty();
    }

    // ── GetObjectArray ─────────────────────────────────────────────────────────

    [Fact]
    public void GetObjectArray_JsonElementArray_ReturnsObjectElements()
    {
        // Arrange — simulate a JSON array of two objects as produced by the model
        var doc = JsonDocument.Parse("""{"nodes":[{"id":"A","task":"T1"},{"id":"B","task":"T2"}]}""");
        var args = new Dictionary<string, object?>
        {
            ["nodes"] = doc.RootElement.GetProperty("nodes")
        };

        // Act
        var result = ToolArgumentHelper.GetObjectArray(args, "nodes");

        // Assert
        result.Should().HaveCount(2);
        result[0].GetProperty("id").GetString().Should().Be("A");
        result[1].GetProperty("id").GetString().Should().Be("B");
    }

    [Fact]
    public void GetObjectArray_MissingKey_ReturnsEmpty()
    {
        var args = new Dictionary<string, object?> { ["other"] = "value" };

        var result = ToolArgumentHelper.GetObjectArray(args, "nodes");

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetObjectArray_NonArrayValue_ReturnsEmpty()
    {
        // Arrange — value is a JSON string, not an array
        var doc = JsonDocument.Parse("""{"nodes":"not-an-array"}""");
        var args = new Dictionary<string, object?>
        {
            ["nodes"] = doc.RootElement.GetProperty("nodes")
        };

        var result = ToolArgumentHelper.GetObjectArray(args, "nodes");

        result.Should().BeEmpty();
    }
}
