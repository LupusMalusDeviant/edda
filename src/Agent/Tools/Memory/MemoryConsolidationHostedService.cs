using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Opt-in background service (issue C10) that periodically consolidates every user's episodic memory so
/// faded/duplicate memories are pruned without waiting for a manual <c>consolidate_memory</c> call.
/// <para>
/// Disabled by default: with <c>MEMORY_CONSOLIDATION_INTERVAL_HOURS</c> ≤ 0 the loop never starts. A positive
/// interval enables it; each cycle runs after the interval elapses and writes a summary to the audit log.
/// Timing is driven through the injected <see cref="TimeProvider"/> so it is deterministically testable.
/// </para>
/// </summary>
internal sealed class MemoryConsolidationHostedService : IHostedService
{
    private readonly IMemoryConsolidator _consolidator;
    private readonly IAuditLog _auditLog;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MemoryConsolidationHostedService> _logger;
    private readonly TimeSpan _interval;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Initializes a new instance of <see cref="MemoryConsolidationHostedService"/>.</summary>
    /// <param name="consolidator">Shared consolidation logic.</param>
    /// <param name="auditLog">Audit log for cycle summaries.</param>
    /// <param name="timeProvider">Clock abstraction driving the interval (injectable for tests).</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="intervalHours">Hours between cycles; <c>≤ 0</c> disables the service (default off).</param>
    public MemoryConsolidationHostedService(
        IMemoryConsolidator consolidator,
        IAuditLog auditLog,
        TimeProvider timeProvider,
        ILogger<MemoryConsolidationHostedService> logger,
        double intervalHours)
    {
        _consolidator = consolidator;
        _auditLog = auditLog;
        _timeProvider = timeProvider;
        _logger = logger;
        _interval = intervalHours > 0 ? TimeSpan.FromHours(intervalHours) : TimeSpan.Zero;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_interval <= TimeSpan.Zero)
        {
            _logger.LogInformation(
                "Memory consolidation disabled (MEMORY_CONSOLIDATION_INTERVAL_HOURS <= 0) | {Component}", "Memory");
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
        _logger.LogInformation(
            "Memory consolidation enabled every {Hours}h | {Component}", _interval.TotalHours, "Memory");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown grace period elapsed — let the loop unwind on its own.
            }
        }
    }

    /// <summary>The consolidation loop: waits one interval, consolidates all users, repeats until cancelled.</summary>
    /// <param name="ct">Token cancelled on shutdown.</param>
    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_interval, _timeProvider, ct).ConfigureAwait(false);

                try
                {
                    var result = await _consolidator.ConsolidateAllAsync(ct).ConfigureAwait(false);

                    if (result.UsersProcessed > 0)
                    {
                        await _auditLog.LogAsync(
                            AuditEvent.MemoryConsolidated,
                            userId: "system",
                            $"Periodic memory consolidation: {result.UsersProcessed} user(s), "
                            + $"{result.DuplicatesRemoved} duplicate(s) removed, "
                            + $"{result.NearDuplicatesRemoved} near-duplicate(s) removed, {result.FadedRemoved} faded pruned",
                            cancellationToken: ct).ConfigureAwait(false);
                    }

                    _logger.LogInformation(
                        "Memory consolidation cycle done: {Users} user(s), {Removed} removed | {Component}",
                        result.UsersProcessed, result.TotalRemoved, "Memory");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Memory consolidation cycle failed; retrying next interval | {Component}", "Memory");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
