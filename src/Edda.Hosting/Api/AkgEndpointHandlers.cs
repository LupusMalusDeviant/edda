using System.Globalization;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.Gateway.Api;

/// <summary>
/// Pure handler functions for AKG REST endpoints.
/// Extracted from the route-mapping method to make each handler independently testable.
/// </summary>
internal static class AkgEndpointHandlers
{
    /// <summary>
    /// Returns rules visible to the requesting user, with optional filters and pagination.
    /// </summary>
    /// <param name="domain">Filter by domain. Null = all domains.</param>
    /// <param name="type">Filter by rule type. Null = all types.</param>
    /// <param name="tag">Filter by tag. Null = no tag filter.</param>
    /// <param name="skip">Optional number of leading rules to skip (must be non-negative).</param>
    /// <param name="take">
    /// Optional page size (must be within <c>[1, <see cref="PageBounds.MaxTake"/>]</c>). Without any
    /// pagination parameters the full set is returned, capped at <see cref="PageBounds.MaxTake"/>.
    /// </param>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="graph">Knowledge graph service.</param>
    /// <param name="response">HTTP response, used to emit the <c>X-Total-Count</c> header.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 200 OK with the matching (paged) rule list and an <c>X-Total-Count</c> header giving the total
    /// number of matches before paging; 400 if a pagination parameter is out of range.
    /// </returns>
    internal static async Task<IResult> GetRulesAsync(
        string? domain,
        string? type,
        string? tag,
        int? skip,
        int? take,
        IIdentityContext identity,
        IKnowledgeGraph graph,
        HttpResponse response,
        CancellationToken ct)
    {
        var bounds = PageBounds.Resolve(skip, take, out var error);
        if (bounds is null)
            return Results.Problem(detail: error, statusCode: StatusCodes.Status400BadRequest);

        var rules = await graph.GetRulesAsync(domain, type, tag, identity.UserId, ct);
        response.Headers["X-Total-Count"] = rules.Count.ToString(CultureInfo.InvariantCulture);

        var page = rules.Skip(bounds.Skip).Take(bounds.Take).ToList();
        return Results.Ok(page);
    }

    /// <summary>
    /// Returns a single rule by ID, scoped to the requesting user.
    /// </summary>
    /// <param name="id">The rule's kebab-case identifier.</param>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="graph">Knowledge graph service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the rule, or 404 if not found or out of scope.</returns>
    internal static async Task<IResult> GetRuleAsync(
        string id,
        IIdentityContext identity,
        IKnowledgeGraph graph,
        CancellationToken ct)
    {
        var rule = await graph.GetRuleAsync(id, identity.UserId, ct);
        return rule is null ? Results.NotFound() : Results.Ok(rule);
    }

    /// <summary>
    /// Returns the 1-hop graph neighbors of the specified rule.
    /// </summary>
    /// <param name="id">The source rule's identifier.</param>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="graph">Knowledge graph service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the neighbor rule list.</returns>
    internal static async Task<IResult> GetNeighborsAsync(
        string id,
        IIdentityContext identity,
        IKnowledgeGraph graph,
        CancellationToken ct)
    {
        var neighbors = await graph.FindNeighborsAsync(id, identity.UserId, ct);
        return Results.Ok(neighbors);
    }

    /// <summary>
    /// Returns aggregate statistics about the knowledge graph.
    /// </summary>
    /// <param name="graph">Knowledge graph service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with graph statistics.</returns>
    internal static async Task<IResult> GetStatsAsync(
        IKnowledgeGraph graph,
        CancellationToken ct)
    {
        var stats = await graph.GetStatsAsync(ct);
        return Results.Ok(stats);
    }

    /// <summary>
    /// Reloads all rules from the <c>knowledge/</c> directory into Neo4j.
    /// Restricted to admins; the central authorizer (C2) additionally requires the Owner role.
    /// </summary>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="authorizer">Central role enforcement (C2).</param>
    /// <param name="graph">Knowledge graph service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success; 403 when the role does not permit administration.</returns>
    internal static async Task<IResult> ReloadAsync(
        IIdentityContext identity,
        IRuleAuthorizer authorizer,
        IKnowledgeGraph graph,
        CancellationToken ct)
    {
        // C2: the route policy authenticates; the authorizer enforces the tenant role centrally.
        try
        {
            authorizer.EnsureCanAdminister(identity.IsAdmin);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }

        await graph.ReloadAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Creates a user-specific knowledge rule.
    /// The <see cref="KnowledgeRule.OwnerId"/> is always set server-side from the authenticated
    /// identity — the client-supplied value is ignored to prevent privilege escalation.
    /// </summary>
    /// <param name="proposedRule">Rule body as supplied by the client.</param>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="graph">Knowledge graph service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with the persisted rule.</returns>
    internal static async Task<IResult> ProposeRuleAsync(
        KnowledgeRule proposedRule,
        IIdentityContext identity,
        IKnowledgeGraph graph,
        CancellationToken ct)
    {
        // OwnerId is always set server-side; never trust what the client sends.
        var rule    = proposedRule with { OwnerId = identity.UserId };
        var created = await graph.UpsertRuleAsync(rule, ct);
        return Results.Created($"/api/akg/rules/{created.Id}", created);
    }

    /// <summary>
    /// Deletes a knowledge rule. Non-admin users may only delete their own rules.
    /// </summary>
    /// <param name="id">The rule ID to delete.</param>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="graph">Knowledge graph service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 204 No Content on success; 401 if caller has no user identity;
    /// 403 if the caller does not own the rule and is not an admin.
    /// </returns>
    internal static async Task<IResult> DeleteRuleAsync(
        string id,
        IIdentityContext identity,
        IKnowledgeGraph graph,
        CancellationToken ct)
    {
        if (identity.UserId is null)
            return Results.Unauthorized();

        try
        {
            await graph.DeleteRuleAsync(id, identity.UserId, identity.IsAdmin, ct);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    }

    /// <summary>
    /// Compiles an AKG context for the given task description.
    /// </summary>
    /// <param name="task">Free-text description of the current task.</param>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="graph">Knowledge graph service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the compiled context result.</returns>
    internal static async Task<IResult> GetContextAsync(
        string task,
        IIdentityContext identity,
        IKnowledgeGraph graph,
        CancellationToken ct)
    {
        var taskContext = new TaskContext
        {
            Task     = task,
            // Derive concepts from the task (mirrors AgentRuntime.ExtractConcepts) so this debug
            // endpoint reflects the full pipeline — including the F49 entity-fusion phase, which is
            // concept-gated and would otherwise stay invisible here.
            Concepts = task
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim('.', ',', '!', '?', '"', '\'').ToLowerInvariant())
                .Where(w => w.Length > 3)
                .Distinct()
                .ToList(),
            UserId   = identity.UserId,
        };
        var result = await graph.CompileContextAsync(taskContext, ct);
        return Results.Ok(result);
    }

    /// <summary>
    /// Applies a batch tag/priority operation to multiple rules (E8). Non-admins only affect their own rules.
    /// </summary>
    /// <param name="request">The batch request (rule ids, operation, tag/priority).</param>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="batch">Batch service that performs the operation and audits it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the outcome counts; 400 ProblemDetails on an invalid request; 401 without a user.</returns>
    internal static async Task<IResult> BatchUpdateAsync(
        BatchRuleOperationRequest request,
        IIdentityContext identity,
        IRuleBatchService batch,
        CancellationToken ct)
    {
        if (identity.UserId is null)
            return Results.Unauthorized();
        if (request.RuleIds is null || request.RuleIds.Count == 0)
            return Results.Problem(detail: "ruleIds must not be empty.", statusCode: StatusCodes.Status400BadRequest);

        var op = ParseBatchOperation(request, out var error);
        if (op is null)
            return Results.Problem(detail: error, statusCode: StatusCodes.Status400BadRequest);

        var result = await batch.ApplyAsync(op, request.RuleIds, identity.UserId, identity.IsAdmin, ct);
        return Results.Ok(result);
    }

    /// <summary>
    /// Shares a dataset by granting a role to a user (ADR-0014). The Owner/admin gate is enforced inside
    /// <see cref="IDatasetSharingService"/>; the tenant and acting user come from the authenticated identity.
    /// </summary>
    /// <param name="id">The dataset id (provenance head, e.g. <c>git:my-repo</c>).</param>
    /// <param name="request">The grant to apply (target user + role name).</param>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="sharing">Owner-gated dataset sharing service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 on success; 401 without a user; 400 for an empty user or unknown role; 403 when the caller may not share.</returns>
    internal static async Task<IResult> ShareDatasetAsync(
        string id,
        DatasetShareRequest request,
        IIdentityContext identity,
        IDatasetSharingService sharing,
        CancellationToken ct)
    {
        if (identity.UserId is null)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.UserId))
            return Results.Problem(detail: "userId must not be empty.", statusCode: StatusCodes.Status400BadRequest);
        if (!Enum.TryParse<TenantRole>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
            return Results.Problem(
                detail: $"Unknown role '{request.Role}'. Valid roles: Viewer, Editor, Owner.",
                statusCode: StatusCodes.Status400BadRequest);

        try
        {
            await sharing.ShareAsync(id, request.UserId, role, ct);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    }

    /// <summary>
    /// Revokes a user's grant on a dataset (ADR-0014). The Owner/admin gate is enforced inside
    /// <see cref="IDatasetSharingService"/>.
    /// </summary>
    /// <param name="id">The dataset id (provenance head).</param>
    /// <param name="userId">The user whose grant is revoked.</param>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="sharing">Owner-gated dataset sharing service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 on success; 401 without a user; 403 when the caller may not share.</returns>
    internal static async Task<IResult> UnshareDatasetAsync(
        string id,
        string userId,
        IIdentityContext identity,
        IDatasetSharingService sharing,
        CancellationToken ct)
    {
        if (identity.UserId is null)
            return Results.Unauthorized();

        try
        {
            await sharing.UnshareAsync(id, userId, ct);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    }

    /// <summary>Parses a batch request into a <see cref="BatchRuleOperation"/>, or null with an error message.</summary>
    /// <param name="request">The request to parse.</param>
    /// <param name="error">The validation error, or null on success.</param>
    /// <returns>The parsed operation, or null when the request is invalid.</returns>
    private static BatchRuleOperation? ParseBatchOperation(BatchRuleOperationRequest request, out string? error)
    {
        error = null;
        switch (request.Operation?.Trim().ToLowerInvariant())
        {
            case "addtag":
            case "removetag":
                if (string.IsNullOrWhiteSpace(request.Tag))
                {
                    error = "tag is required for addTag/removeTag.";
                    return null;
                }
                return new BatchRuleOperation
                {
                    Type = string.Equals(request.Operation.Trim(), "addTag", StringComparison.OrdinalIgnoreCase)
                        ? BatchRuleOperationType.AddTag
                        : BatchRuleOperationType.RemoveTag,
                    Tag = request.Tag.Trim(),
                };

            case "setpriority":
                if (!Enum.TryParse<RulePriority>(request.Priority, ignoreCase: true, out var priority))
                {
                    error = "priority must be Low, Medium or High for setPriority.";
                    return null;
                }
                return new BatchRuleOperation { Type = BatchRuleOperationType.SetPriority, Priority = priority };

            default:
                error = "operation must be one of: addTag, removeTag, setPriority.";
                return null;
        }
    }
}

/// <summary>Batch-operation request body for <c>POST /api/akg/rules/batch</c> (E8).</summary>
/// <param name="RuleIds">The target rule ids.</param>
/// <param name="Operation">One of <c>addTag</c>, <c>removeTag</c>, <c>setPriority</c>.</param>
/// <param name="Tag">The tag for addTag/removeTag.</param>
/// <param name="Priority">The priority (Low/Medium/High) for setPriority.</param>
internal sealed record BatchRuleOperationRequest(
    IReadOnlyList<string> RuleIds,
    string Operation,
    string? Tag,
    string? Priority);
