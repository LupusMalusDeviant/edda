using System.Text.Json;

namespace Edda.Core.Models;

/// <summary>
/// Result of an individual pipeline step execution.
/// Contains the step status, timing, and a step-specific JSON payload.
/// </summary>
/// <param name="RunId">Unique pipeline run identifier.</param>
/// <param name="Step">The executed pipeline step.</param>
/// <param name="Success">Whether the step completed successfully.</param>
/// <param name="Error">Error message if the step failed, null otherwise.</param>
/// <param name="DurationMs">Step execution duration in milliseconds.</param>
/// <param name="CompletedAt">UTC timestamp of step completion.</param>
/// <param name="Payload">Step-specific result data as JSON. Content varies by step type.</param>
public sealed record StepResult(
    string RunId,
    PipelineStep Step,
    bool Success,
    string? Error,
    long DurationMs,
    DateTimeOffset CompletedAt,
    JsonDocument? Payload);

/// <summary>
/// Request body for triggering an individual pipeline step via the external API.
/// </summary>
/// <param name="Config">Project configuration override. Required for the first step, optional for subsequent.</param>
/// <param name="WebhookUrl">Override webhook callback URL for this run (optional).</param>
/// <param name="WebhookSecret">HMAC secret for signing webhook callbacks (optional).</param>
public sealed record ExternalStepRequest(
    DevProjectConfig? Config = null,
    string? WebhookUrl = null,
    string? WebhookSecret = null);

/// <summary>
/// Response body for external API step trigger and status queries.
/// </summary>
/// <param name="RunId">Unique pipeline run identifier.</param>
/// <param name="Slug">ASPS project slug.</param>
/// <param name="Step">The pipeline step that was triggered or queried.</param>
/// <param name="Status">Execution status: "completed", "accepted" (async), or "failed".</param>
/// <param name="Result">Step result with payload (null for async "accepted" responses).</param>
/// <param name="Error">Error description if status is "failed".</param>
public sealed record ExternalStepResponse(
    string RunId,
    string Slug,
    PipelineStep Step,
    string Status,
    StepResult? Result,
    string? Error);

/// <summary>
/// Webhook callback payload sent to the ASPS.ai platform after each step.
/// The JSON body is signed with HMAC-SHA256 and sent as HTTP POST.
/// </summary>
/// <param name="RunId">Unique pipeline run identifier.</param>
/// <param name="Slug">ASPS project slug.</param>
/// <param name="Event">Event type: "step.completed", "step.failed", "pipeline.completed".</param>
/// <param name="Step">The pipeline step that triggered this callback.</param>
/// <param name="Result">Full step result including payload.</param>
/// <param name="Timestamp">UTC timestamp of the webhook dispatch.</param>
public sealed record AspsWebhookPayload(
    string RunId,
    string Slug,
    string Event,
    PipelineStep Step,
    StepResult Result,
    DateTimeOffset Timestamp);

/// <summary>
/// Metadata for an ASPS external API key.
/// The actual secret is stored encrypted in ICredentialStore.
/// </summary>
/// <param name="KeyId">Unique key identifier (used in X-Api-Key header).</param>
/// <param name="UserId">The user ID this key authenticates as.</param>
/// <param name="Description">Human-readable description of the key purpose.</param>
/// <param name="WebhookUrl">Default webhook URL for runs authenticated with this key.</param>
/// <param name="WebhookSecret">HMAC secret for outbound webhook signing (Edda → ASPS direction).</param>
/// <param name="CreatedAt">UTC timestamp of key creation.</param>
/// <param name="InboundSecret">
/// HMAC secret for verifying inbound requests (ASPS → Edda direction).
/// This is the API key secret the ASPS platform uses in the X-Asps-Signature header.
/// Falls back to <see cref="WebhookSecret"/> for keys created before this field was introduced.
/// </param>
public sealed record AspsApiKeyMetadata(
    string KeyId,
    string UserId,
    string Description,
    string? WebhookUrl,
    string? WebhookSecret,
    DateTimeOffset CreatedAt,
    string? InboundSecret = null);

/// <summary>
/// Response returned when creating a new API key.
/// Contains the secret in plaintext — this is the only time it is exposed.
/// </summary>
/// <param name="KeyId">The key identifier to use in X-Api-Key header.</param>
/// <param name="Secret">The HMAC secret for request signing. Store securely — not retrievable later.</param>
/// <param name="UserId">The user ID this key authenticates as.</param>
public sealed record AspsApiKeyCreatedResponse(
    string KeyId,
    string Secret,
    string UserId);

/// <summary>
/// Request body for the ASPS external feedback endpoint.
/// Sent by the ASPS prototype plugin when a user submits feedback on the HTML prototype.
/// </summary>
/// <param name="RunId">The active pipeline run ID.</param>
/// <param name="Page">Page path within the prototype (e.g. "/dashboard").</param>
/// <param name="ElementSelector">CSS selector of the annotated element. Null for general feedback.</param>
/// <param name="FeedbackText">The user's feedback text.</param>
/// <param name="Priority">Feedback priority: "critical", "important", or "nice-to-have".</param>
public sealed record ExternalFeedbackRequest(
    string RunId,
    string Page,
    string? ElementSelector,
    string FeedbackText,
    string Priority = "important");

/// <summary>
/// Request body for the ASPS external approve endpoint.
/// Sent by the ASPS plugin when the stakeholder approves the prototype.
/// </summary>
/// <param name="RunId">The active pipeline run ID.</param>
public sealed record ExternalApproveRequest(string RunId);
