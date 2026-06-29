using Edda.Core.Models;
using Edda.Security.Taint;

namespace Edda.Security.Tests.Taint;

/// <summary>
/// Unit tests for <see cref="TaintSinkRegistry"/>.
/// Verifies default sink configuration and runtime extension.
/// </summary>
public sealed class TaintSinkRegistryTests
{
    [Fact]
    public void DefaultConfig_HasExpectedSinks()
    {
        var registry = new TaintSinkRegistry();

        // shell_execute must forbid ExternalNetwork + UntrustedAgent
        var shellLabel = registry.GetForbiddenLabels("shell_execute");
        shellLabel.Should().HaveFlag(TaintLabel.ExternalNetwork);
        shellLabel.Should().HaveFlag(TaintLabel.UntrustedAgent);

        // python_code_interpreter: same as shell
        var pythonLabel = registry.GetForbiddenLabels("python_code_interpreter");
        pythonLabel.Should().HaveFlag(TaintLabel.ExternalNetwork);
        pythonLabel.Should().HaveFlag(TaintLabel.UntrustedAgent);

        // manage_credentials: must forbid ExternalNetwork + UntrustedAgent
        var credLabel = registry.GetForbiddenLabels("manage_credentials");
        credLabel.Should().HaveFlag(TaintLabel.ExternalNetwork);
        credLabel.Should().HaveFlag(TaintLabel.UntrustedAgent);

        // http_request: must forbid Secret + Pii
        var httpLabel = registry.GetForbiddenLabels("http_request");
        httpLabel.Should().HaveFlag(TaintLabel.Secret);
        httpLabel.Should().HaveFlag(TaintLabel.Pii);

        // Unknown tool: no restrictions
        var unknownLabel = registry.GetForbiddenLabels("get_time");
        unknownLabel.Should().Be(TaintLabel.None);
    }

    [Fact]
    public void AddSink_OverridesDefaultAndAddsNew()
    {
        var registry = new TaintSinkRegistry();

        // Override an existing sink
        registry.AddSink("shell_execute", TaintLabel.Pii);
        registry.GetForbiddenLabels("shell_execute").Should().Be(TaintLabel.Pii);

        // Add a new custom sink
        registry.AddSink("my_custom_tool", TaintLabel.Secret | TaintLabel.ExternalNetwork);
        var customLabel = registry.GetForbiddenLabels("my_custom_tool");
        customLabel.Should().HaveFlag(TaintLabel.Secret);
        customLabel.Should().HaveFlag(TaintLabel.ExternalNetwork);
    }
}
