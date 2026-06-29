using Edda.Core.Abstractions;
using Edda.Security.Audit;
using Edda.Security.Configuration;
using Edda.Security.Credentials;
using Edda.Security.OutputFilter;
using Edda.Security.Sanitization;
using Edda.Security.Taint;
using Microsoft.Extensions.DependencyInjection;

namespace Edda.Security.DependencyInjection;

/// <summary>
/// Extension methods for registering Security services with the DI container.
/// Called from Gateway/Program.cs as part of the composition root.
/// </summary>
public static class SecurityServiceExtensions
{
    /// <summary>
    /// Registers all Security services: InputSanitizer, SecretRedactor,
    /// IAuditLog (HmacAuditLog), ICredentialStore (AesCredentialStore),
    /// ISettingsService (FileSettingsService) for persisted runtime configuration,
    /// and TaintSinkRegistry for data-flow security enforcement.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecurityServices(this IServiceCollection services)
    {
        services.AddSingleton<IInputSanitizer, InputSanitizer>();
        services.AddSingleton<ISecretRedactor, SecretRedactor>();
        services.AddSingleton<IAuditLog, HmacAuditLog>();
        services.AddSingleton<ICredentialStore, AesCredentialStore>();
        services.AddSingleton<ISettingsService, FileSettingsService>();
        services.AddSingleton(_ => TaintSinkRegistry.FromEnvironment());
        services.AddSingleton<IMerkleAuditVerifier, MerkleAuditVerifier>();
        return services;
    }
}
