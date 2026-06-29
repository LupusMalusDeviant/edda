using Edda.Core.Abstractions;
using Edda.Sandboxing.Docker;
using Edda.Sandboxing.Wasm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.DependencyInjection;

/// <summary>
/// Extension methods for registering Sandboxing services with the DI container.
/// Called from Gateway/Program.cs as part of the composition root.
/// </summary>
public static class SandboxingServiceExtensions
{
    /// <summary>DI key for TDK sandbox factories (network-isolated, <c>--network=none</c>).</summary>
    public const string TdkSandboxKey = "tdk";

    /// <summary>DI key for tool sandbox factories (network-capable, configurable via <c>SANDBOX_NETWORK_MODE</c>).</summary>
    public const string ToolSandboxKey = "tool";

    /// <summary>
    /// Registers the appropriate <see cref="ISandboxFactory"/> based on the
    /// <c>TDK_SANDBOX_TYPE</c> environment variable.
    /// <para>
    /// Two keyed registrations are created when using Docker:
    /// <list type="bullet">
    ///   <item><c>"tdk"</c> — fully isolated (<c>--network=none</c>), used by TDK validators.</item>
    ///   <item><c>"tool"</c> — network mode from <c>SANDBOX_NETWORK_MODE</c> env var (default: <c>bridge</c>),
    ///         used by custom tools and code interpreter.</item>
    /// </list>
    /// The non-keyed <see cref="ISandboxFactory"/> resolves to the TDK sandbox for backwards compatibility.
    /// </para>
    /// <list type="bullet">
    ///   <item><c>docker</c> (default) — uses <see cref="DockerSandboxFactory"/> with a real Docker daemon.</item>
    ///   <item><c>wasm</c> — uses <see cref="WasmSandboxFactory"/> with a local Python subprocess.</item>
    ///   <item>any other value — falls back to <see cref="NullSandboxFactory"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSandboxingServices(this IServiceCollection services)
    {
        var sandboxType = Environment.GetEnvironmentVariable("TDK_SANDBOX_TYPE")
            ?? "docker";

        var pythonImage = Environment.GetEnvironmentVariable("SANDBOX_PYTHON_IMAGE");
        var toolNetworkMode = Environment.GetEnvironmentVariable("SANDBOX_NETWORK_MODE")
            ?? DefaultDockerContainerOperations.NetworkBridge;

        switch (sandboxType.ToLowerInvariant())
        {
            case "docker":
                // TDK container ops: fully isolated (network=none)
                services.AddKeyedSingleton<IDockerContainerOperations>(TdkSandboxKey, (sp, _) =>
                    new DefaultDockerContainerOperations(
                        sp.GetRequiredService<ILogger<DefaultDockerContainerOperations>>(),
                        pythonImage,
                        DefaultDockerContainerOperations.NetworkNone));

                // Tool container ops: configurable network (default: bridge)
                services.AddKeyedSingleton<IDockerContainerOperations>(ToolSandboxKey, (sp, _) =>
                    new DefaultDockerContainerOperations(
                        sp.GetRequiredService<ILogger<DefaultDockerContainerOperations>>(),
                        pythonImage,
                        toolNetworkMode));

                // Non-keyed IDockerContainerOperations → TDK (backwards compat)
                services.AddSingleton<IDockerContainerOperations>(sp =>
                    sp.GetRequiredKeyedService<IDockerContainerOperations>(TdkSandboxKey));

                // Keyed ISandboxFactory: TDK (network=none)
                services.AddKeyedSingleton<ISandboxFactory>(TdkSandboxKey, (sp, _) =>
                    new DockerSandboxFactory(
                        sp.GetRequiredKeyedService<IDockerContainerOperations>(TdkSandboxKey),
                        sp.GetRequiredService<ILogger<DockerSandbox>>()));

                // Keyed ISandboxFactory: Tool (network=bridge by default)
                services.AddKeyedSingleton<ISandboxFactory>(ToolSandboxKey, (sp, _) =>
                    new DockerSandboxFactory(
                        sp.GetRequiredKeyedService<IDockerContainerOperations>(ToolSandboxKey),
                        sp.GetRequiredService<ILogger<DockerSandbox>>()));

                // Non-keyed ISandboxFactory → TDK (backwards compat for TdkEngine)
                services.AddSingleton<ISandboxFactory>(sp =>
                    sp.GetRequiredKeyedService<ISandboxFactory>(TdkSandboxKey));
                break;

            case "wasm":
                services.AddSingleton<IWasmScriptRunner>(sp =>
                    new DefaultWasmScriptRunner(
                        sp.GetRequiredService<ILogger<DefaultWasmScriptRunner>>()));
                services.AddSingleton<ISandboxFactory>(sp =>
                    new WasmSandboxFactory(
                        sp.GetRequiredService<IWasmScriptRunner>(),
                        sp.GetRequiredService<ILogger<WasmSandbox>>()));
                // Wasm does not differentiate TDK vs Tool — same factory for both
                services.AddKeyedSingleton<ISandboxFactory>(TdkSandboxKey, (sp, _) =>
                    sp.GetRequiredService<ISandboxFactory>());
                services.AddKeyedSingleton<ISandboxFactory>(ToolSandboxKey, (sp, _) =>
                    sp.GetRequiredService<ISandboxFactory>());
                break;

            default:
                services.AddSingleton<ISandboxFactory, NullSandboxFactory>();
                services.AddKeyedSingleton<ISandboxFactory>(TdkSandboxKey, (sp, _) =>
                    sp.GetRequiredService<ISandboxFactory>());
                services.AddKeyedSingleton<ISandboxFactory>(ToolSandboxKey, (sp, _) =>
                    sp.GetRequiredService<ISandboxFactory>());
                break;
        }

        // .NET Build Sandbox (used by SelfDev pipeline, always Docker-based)
        var sdkImage = Environment.GetEnvironmentVariable("DOTNET_SDK_IMAGE");
        services.AddSingleton<IDotNetBuildContainerOps>(sp =>
            new DefaultDotNetBuildContainerOps(
                sp.GetRequiredService<ILogger<DefaultDotNetBuildContainerOps>>(),
                sdkImage));
        services.AddSingleton<IDotNetBuildSandboxFactory>(sp =>
            new DotNetBuildSandboxFactory(
                sp.GetRequiredService<IDotNetBuildContainerOps>(),
                sp.GetRequiredService<ILogger<DotNetBuildSandbox>>()));

        return services;
    }
}
