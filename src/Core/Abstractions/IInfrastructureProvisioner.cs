using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Provisions infrastructure dependencies (e.g. graph database containers) at startup.
/// Implementations check whether provisioning is needed and create Docker containers
/// with the appropriate image, ports, volumes, and environment variables.
/// </summary>
public interface IInfrastructureProvisioner
{
    /// <summary>
    /// Determines whether the target infrastructure needs provisioning.
    /// Returns <see langword="false"/> if the service is already reachable,
    /// if the URI is non-localhost (external managed), or if Docker is unavailable.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if provisioning should be attempted.</returns>
    Task<bool> NeedsProvisioningAsync(CancellationToken ct = default);

    /// <summary>
    /// Provisions the infrastructure (e.g. creates and starts a Docker container).
    /// Ensures the network exists, creates or starts the container,
    /// and waits for health checks to pass.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result including success status, connection URI, and container ID.</returns>
    Task<ProvisioningResult> ProvisionAsync(CancellationToken ct = default);
}
