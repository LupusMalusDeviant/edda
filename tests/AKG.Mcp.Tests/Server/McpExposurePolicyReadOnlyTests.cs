using Edda.AKG.Mcp.Server;

namespace Edda.AKG.Mcp.Tests.Server;

/// <summary>
/// Verifies the read-only guarantee of <see cref="McpExposurePolicy"/>: external MCP clients may only
/// reach read-only tools; mutating tools stay blocked unless write access is explicitly enabled.
/// </summary>
public sealed class McpExposurePolicyReadOnlyTests
{
    [Theory]
    [InlineData("search_memory")]
    [InlineData("list_memory")]
    public void IsExposed_DefaultReadOnlyTool_Exposed(string tool)
        => McpExposurePolicy.FromConfiguration(null).IsExposed(tool).Should().BeTrue();

    [Theory]
    [InlineData("manage_memory")]
    [InlineData("manage_userdata")]
    [InlineData("manage_learnings")]
    public void IsExposed_WriteToolEvenIfAllowListed_BlockedByDefault(string tool)
    {
        // Tool is explicitly allow-listed ...
        var policy = new McpExposurePolicy([tool]);

        // ... but write tools stay blocked to keep external MCP access read-only.
        policy.IsExposed(tool).Should().BeFalse();
    }

    [Fact]
    public void IsExposed_WriteTool_AllowedWhenWriteFlagSet()
    {
        var policy = new McpExposurePolicy(["manage_memory"], allowWriteTools: true);

        policy.IsExposed("manage_memory").Should().BeTrue();
    }

    [Fact]
    public void FromConfiguration_WriteToolInCsv_StillBlockedByDefault()
    {
        var policy = McpExposurePolicy.FromConfiguration("list_memory,manage_memory");

        policy.IsExposed("list_memory").Should().BeTrue();
        policy.IsExposed("manage_memory").Should().BeFalse();
    }
}
