namespace Edda.Core.Models;

/// <summary>
/// Single structured log entry scoped to an internal project id. Written by
/// pipeline components (AspsStepExecutor, PrototypeOrchestrator, sub-agents,
/// GitLab deployer, smoke test) and surfaced in the Web-UI via
/// <c>GET /api/projects/{id}/log</c>.
/// </summary>
/// <param name="Id">Monotonically increasing DB-side id.</param>
/// <param name="Timestamp">UTC time the event happened.</param>
/// <param name="ProjectId">Internal project identifier.</param>
/// <param name="Level">Log severity: <c>"info"</c>, <c>"warn"</c>, or <c>"error"</c>.</param>
/// <param name="Component">Producer component (e.g. <c>"PrototypeOrchestrator"</c>).</param>
/// <param name="Message">Human-readable event text.</param>
/// <param name="DataJson">Optional structured payload as JSON string (e.g. phase durations, file counts).</param>
public sealed record ProjectLogEntry(
    long Id,
    DateTimeOffset Timestamp,
    string ProjectId,
    string Level,
    string Component,
    string Message,
    string? DataJson);
