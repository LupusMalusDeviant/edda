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
}
