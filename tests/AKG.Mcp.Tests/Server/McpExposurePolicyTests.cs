using Edda.AKG.Mcp.Server;

namespace Edda.AKG.Mcp.Tests.Server;

public class McpExposurePolicyTests
{
    [Fact]
    public void IsExposed_AllowListedTool_ReturnsTrue()
    {
        var policy = new McpExposurePolicy(["search_memory"]);
        policy.IsExposed("search_memory").Should().BeTrue();
    }

    [Fact]
    public void IsExposed_NonAllowListedTool_ReturnsFalse()
    {
        var policy = new McpExposurePolicy(["search_memory"]);
        policy.IsExposed("shell_execute").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsExposed_NullOrBlank_ReturnsFalse(string? toolName)
    {
        var policy = new McpExposurePolicy(["search_memory"]);
        policy.IsExposed(toolName).Should().BeFalse();
    }

    [Fact]
    public void IsExposed_DifferentCasing_ReturnsTrue()
    {
        var policy = new McpExposurePolicy(["search_memory"]);
        policy.IsExposed("Search_Memory").Should().BeTrue();
    }

    [Fact]
    public void FromConfiguration_Null_UsesSafeDefaults()
    {
        var policy = McpExposurePolicy.FromConfiguration(null);
        policy.AllowedToolNames.Should().BeEquivalentTo(McpExposurePolicy.DefaultExposedTools);
    }

    [Fact]
    public void FromConfiguration_Blank_UsesSafeDefaults()
    {
        var policy = McpExposurePolicy.FromConfiguration("   ");
        policy.AllowedToolNames.Should().BeEquivalentTo(McpExposurePolicy.DefaultExposedTools);
    }

    [Fact]
    public void FromConfiguration_Csv_ParsesAndTrimsNames()
    {
        var policy = McpExposurePolicy.FromConfiguration("tool_a, tool_b ,tool_c");
        policy.AllowedToolNames.Should().BeEquivalentTo(["tool_a", "tool_b", "tool_c"]);
    }

    [Fact]
    public void FromConfiguration_OnlySeparators_UsesSafeDefaults()
    {
        // Non-blank but yields no names after RemoveEmptyEntries → must fall back to defaults.
        var policy = McpExposurePolicy.FromConfiguration(",, ,");
        policy.AllowedToolNames.Should().BeEquivalentTo(McpExposurePolicy.DefaultExposedTools);
    }

    [Fact]
    public void DefaultExposedTools_ExcludeDangerousTools()
    {
        // Security regression guard: the safe defaults must never include mutating/dangerous tools.
        McpExposurePolicy.DefaultExposedTools.Should().NotContain(
        [
            "shell_execute",
            "python_code_interpreter",
            "manage_credentials",
            "manage_files",
            "manage_plugins",
            "docker_management"
        ]);
    }

    [Theory]
    [InlineData("remember")]
    [InlineData("forget")]
    [InlineData("consolidate_memory")]
    [InlineData("rate_memory")]
    public void IsExposed_EpisodicWriteTools_BlockedByDefault_EvenWhenAllowListed(string toolName)
    {
        // Episodic-memory write tools stay default-deny over MCP (M3 / ADR-0011 safety-first; E2 rate_memory).
        var policy = new McpExposurePolicy([toolName], allowWriteTools: false);
        policy.IsExposed(toolName).Should().BeFalse();
    }

    [Theory]
    [InlineData("remember")]
    [InlineData("forget")]
    [InlineData("consolidate_memory")]
    [InlineData("rate_memory")]
    public void IsExposed_EpisodicWriteTools_Exposed_WhenWriteAccessEnabledAndAllowListed(string toolName)
    {
        var policy = new McpExposurePolicy([toolName], allowWriteTools: true);
        policy.IsExposed(toolName).Should().BeTrue();
    }

    [Fact]
    public void DefaultExposedTools_DoNotIncludeRateMemory_ReadOnlyDefault()
    {
        // E2: rate_memory is a write tool → never in the read-only default surface.
        McpExposurePolicy.DefaultExposedTools.Should().NotContain("rate_memory");
    }

    [Fact]
    public void IsExposed_Recall_ReadOnly_Exposed_WhenAllowListed_WithoutWriteAccess()
    {
        // recall is read-only + opt-in: exposable when allow-listed, no write access required.
        var policy = new McpExposurePolicy(["recall"], allowWriteTools: false);
        policy.IsExposed("recall").Should().BeTrue();
    }

    [Fact]
    public void DefaultExposedTools_DoNotIncludeRecall_OptInOnly()
    {
        // ADR-0011: recall is opt-in, never in the default read allow-list.
        McpExposurePolicy.DefaultExposedTools.Should().NotContain("recall");
    }
}
