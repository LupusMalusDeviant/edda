using Edda.AKG.Mcp.DependencyInjection;
using Edda.Core.Models;

namespace Edda.AKG.Mcp.Tests;

/// <summary>
/// Unit tests for the MCP exposure resolution (settings → env → defaults) that backs the live,
/// UI-configurable MCP server settings.
/// </summary>
public sealed class McpSettingsResolutionTests
{
    [Fact]
    public void Disabled_ExposesNothing()
    {
        var policy = McpServiceExtensions.ResolveMcpPolicy(
            new McpSettings { Enabled = false }, "list_memory", false);

        policy.IsExposed("list_memory").Should().BeFalse();
    }

    [Fact]
    public void ExposedToolsFromSettings_WinOverEnv()
    {
        var policy = McpServiceExtensions.ResolveMcpPolicy(
            new McpSettings { ExposedTools = ["search_memory"] }, "tdk_validate", false);

        policy.IsExposed("search_memory").Should().BeTrue();
        policy.IsExposed("tdk_validate").Should().BeFalse();
    }

    [Fact]
    public void NoSettings_FallsBackToEnvCsv()
    {
        var policy = McpServiceExtensions.ResolveMcpPolicy(new McpSettings(), "list_memory", false);

        policy.IsExposed("list_memory").Should().BeTrue();
        policy.IsExposed("search_memory").Should().BeFalse();
    }

    [Fact]
    public void NoSettingsNoEnv_FallsBackToReadOnlyDefaults()
    {
        var policy = McpServiceExtensions.ResolveMcpPolicy(new McpSettings(), null, false);

        policy.IsExposed("search_memory").Should().BeTrue();
        policy.IsExposed("manage_memory").Should().BeFalse();
    }

    [Fact]
    public void WriteTool_BlockedUnlessAllowWriteEnabled()
    {
        var blocked = McpServiceExtensions.ResolveMcpPolicy(
            new McpSettings { ExposedTools = ["manage_memory"] }, null, false);
        blocked.IsExposed("manage_memory").Should().BeFalse();

        var allowed = McpServiceExtensions.ResolveMcpPolicy(
            new McpSettings { ExposedTools = ["manage_memory"], AllowWriteTools = true }, null, false);
        allowed.IsExposed("manage_memory").Should().BeTrue();
    }
}
