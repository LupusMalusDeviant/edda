namespace Edda.Core.Exceptions;

/// <summary>
/// Thrown when the system cannot start due to missing or invalid configuration.
/// Results in a non-zero exit code and prevents the HTTP server from starting.
/// </summary>
public sealed class ConfigurationException : EddaException
{
    /// <summary>The configuration key or section that is missing or invalid.</summary>
    public string ConfigKey { get; }

    /// <summary>Initializes a new ConfigurationException.</summary>
    /// <param name="configKey">The problematic configuration key.</param>
    /// <param name="message">Human-readable description of the configuration problem.</param>
    public ConfigurationException(string configKey, string message)
        : base("Configuration", message)
    {
        ConfigKey = configKey;
    }
}
