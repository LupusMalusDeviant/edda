using Edda.Core.Abstractions;
using Edda.Sandboxing.DependencyInjection;
using Edda.Sandboxing.Docker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.Tests;

/// <summary>
/// Verifies that <see cref="SandboxingServiceExtensions.AddSandboxingServices"/>
/// registers keyed and non-keyed sandbox factories correctly.
/// </summary>
public class SandboxingServiceExtensionsTests
{
    [Fact]
    public void TdkSandboxKey_HasExpectedValue()
    {
        SandboxingServiceExtensions.TdkSandboxKey.Should().Be("tdk");
    }

    [Fact]
    public void ToolSandboxKey_HasExpectedValue()
    {
        SandboxingServiceExtensions.ToolSandboxKey.Should().Be("tool");
    }

    [Fact]
    public void AddSandboxingServices_NullSandbox_RegistersNonKeyedFactory()
    {
        Environment.SetEnvironmentVariable("TDK_SANDBOX_TYPE", "null");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSandboxingServices();
            var sp = services.BuildServiceProvider();

            var factory = sp.GetService<ISandboxFactory>();
            factory.Should().NotBeNull();
            factory.Should().BeOfType<NullSandboxFactory>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TDK_SANDBOX_TYPE", null);
        }
    }

    [Fact]
    public void AddSandboxingServices_NullSandbox_RegistersKeyedTdkFactory()
    {
        Environment.SetEnvironmentVariable("TDK_SANDBOX_TYPE", "null");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSandboxingServices();
            var sp = services.BuildServiceProvider();

            var factory = sp.GetRequiredKeyedService<ISandboxFactory>(SandboxingServiceExtensions.TdkSandboxKey);
            factory.Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TDK_SANDBOX_TYPE", null);
        }
    }

    [Fact]
    public void AddSandboxingServices_NullSandbox_RegistersKeyedToolFactory()
    {
        Environment.SetEnvironmentVariable("TDK_SANDBOX_TYPE", "null");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSandboxingServices();
            var sp = services.BuildServiceProvider();

            var factory = sp.GetRequiredKeyedService<ISandboxFactory>(SandboxingServiceExtensions.ToolSandboxKey);
            factory.Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TDK_SANDBOX_TYPE", null);
        }
    }

    [Fact]
    public void AddSandboxingServices_Wasm_RegistersKeyedFactories()
    {
        Environment.SetEnvironmentVariable("TDK_SANDBOX_TYPE", "wasm");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSandboxingServices();
            var sp = services.BuildServiceProvider();

            var tdkFactory = sp.GetRequiredKeyedService<ISandboxFactory>(SandboxingServiceExtensions.TdkSandboxKey);
            var toolFactory = sp.GetRequiredKeyedService<ISandboxFactory>(SandboxingServiceExtensions.ToolSandboxKey);
            var defaultFactory = sp.GetRequiredService<ISandboxFactory>();

            tdkFactory.Should().NotBeNull();
            toolFactory.Should().NotBeNull();
            defaultFactory.Should().NotBeNull();
            tdkFactory.SandboxType.Should().Be("wasm");
            toolFactory.SandboxType.Should().Be("wasm");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TDK_SANDBOX_TYPE", null);
        }
    }
}
