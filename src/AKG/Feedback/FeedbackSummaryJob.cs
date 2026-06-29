using Edda.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Feedback;

/// <summary>
/// Background service that recalculates AKG rule confidence multipliers once per day at 02:00 UTC.
/// Calls <see cref="IRuleFeedbackService.RecalculateAllAsync"/> to recompute and persist
/// updated multipliers for all rules with accumulated feedback data.
/// </summary>
internal sealed class FeedbackSummaryJob : BackgroundService
{
    private static readonly TimeSpan RecalculationHour = TimeSpan.FromHours(2); // 02:00 UTC

    private readonly IRuleFeedbackService _feedbackService;
    private readonly TimeProvider _time;
    private readonly ILogger<FeedbackSummaryJob> _logger;

    /// <summary>
    /// Initializes a new <see cref="FeedbackSummaryJob"/>.
    /// </summary>
    /// <param name="feedbackService">Feedback service used to trigger recalculation.</param>
    /// <param name="time">Time provider for computing next-run delay.</param>
    /// <param name="logger">Structured logger.</param>
    public FeedbackSummaryJob(
        IRuleFeedbackService feedbackService,
        TimeProvider time,
        ILogger<FeedbackSummaryJob> logger)
    {
        _feedbackService = feedbackService;
        _time            = time;
        _logger          = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "FeedbackSummaryJob started. Recalculation runs daily at 02:00 UTC | {Component}",
            "AKG.Feedback");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRun();
            _logger.LogDebug(
                "Next confidence recalculation in {Delay:hh\\:mm} | {Component}",
                delay, "AKG.Feedback");

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                _logger.LogInformation(
                    "Starting daily confidence recalculation | {Component}", "AKG.Feedback");

                await _feedbackService.RecalculateAllAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "Daily confidence recalculation failed | {Component}", "AKG.Feedback");
                // Continue loop — will retry next day
            }
        }

        _logger.LogInformation(
            "FeedbackSummaryJob stopped | {Component}", "AKG.Feedback");
    }

    /// <summary>
    /// Computes how long to wait until the next 02:00 UTC run.
    /// If it is already past 02:00 today, schedules for 02:00 tomorrow.
    /// </summary>
    private TimeSpan ComputeDelayUntilNextRun()
    {
        var now = _time.GetUtcNow();
        var nextRun = now.Date + RecalculationHour;
        if (nextRun <= now.UtcDateTime)
            nextRun = nextRun.AddDays(1);

        var delay = nextRun - now.UtcDateTime;
        // Clamp to sensible range (between 1 minute and 25 hours)
        return TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 60, 90_000));
    }
}
