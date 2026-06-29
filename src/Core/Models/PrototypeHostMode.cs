namespace Edda.Core.Models;

/// <summary>
/// Selects how generated prototype URLs are published to ASPS.ai.
/// </summary>
/// <remarks>
/// Controlled by the <c>PROTOTYPE_HOST_MODE</c> environment variable.
/// The GitLab deploy path always runs when a deployer is configured (so
/// GitLab remains an up-to-date archive), but only the <see cref="Gitlab"/>
/// mode surfaces the GitLab Pages URL as the primary link. In <see cref="Local"/>
/// mode the URL points to the Edda gateway's built-in
/// <c>/prototype/{projectId}</c> static-file endpoint — required when the
/// GitLab instance restricts Pages visibility and external consumers cannot
/// reach the Pages URL.
/// </remarks>
public enum PrototypeHostMode
{
    /// <summary>
    /// Primary URL is served by the Edda gateway itself
    /// (<c>{GATEWAY_BASE_URL}/prototype/{projectId}</c>). Publicly reachable
    /// without authentication. GitLab deploy is best-effort archive only —
    /// failures are logged but do not break the pipeline.
    /// </summary>
    Local = 0,

    /// <summary>
    /// Primary URL is the GitLab Pages deployment URL. GitLab deploy failure
    /// is a hard error that fails the prototype step.
    /// </summary>
    Gitlab = 1,
}
