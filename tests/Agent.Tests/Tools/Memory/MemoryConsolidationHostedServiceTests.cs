using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

/// <summary>
/// Unit tests for <see cref="MemoryConsolidationHostedService"/> (C10): fires on the configured interval and
/// stays off by default — driven deterministically via <see cref="FakeTimeProvider"/>.
/// </summary>
public class MemoryConsolidationHostedServiceTests
{
    private readonly Mock<IMemoryConsolidator> _consolidator = new();
    private readonly Mock<IAuditLog> _audit = new();
    private readonly FakeTimeProvider _time = new();

    private MemoryConsolidationHostedService Service(double intervalHours) =>
        new(_consolidator.Object, _audit.Object, _time,
            NullLogger<MemoryConsolidationHostedService>.Instance, intervalHours);

    [Fact]
    public async Task Enabled_FiresAfterInterval_RunsConsolidationAndAudits()
    {
        var fired = new TaskCompletionSource();
        _consolidator.Setup(c => c.ConsolidateAllAsync(It.IsAny<CancellationToken>()))
            .Callback(() => fired.TrySetResult())
            .ReturnsAsync(new MemoryConsolidationResult(2, 3, 1));

        var sut = Service(intervalHours: 1);
        await sut.StartAsync(CancellationToken.None);

        // Advance the fake clock by the interval until the cycle fires — no real wall-clock wait.
        for (var i = 0; i < 50 && !fired.Task.IsCompleted; i++)
        {
            _time.Advance(TimeSpan.FromHours(1));
            await Task.Delay(10);
        }

        fired.Task.IsCompleted.Should().BeTrue("the service must run a consolidation cycle after the interval");
        await sut.StopAsync(CancellationToken.None);

        _consolidator.Verify(c => c.ConsolidateAllAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _audit.Verify(a => a.LogAsync(
            AuditEvent.MemoryConsolidated, "system", It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "each cycle writes a summary to the audit log");
    }

    [Fact]
    public async Task Disabled_DefaultIntervalZero_NeverConsolidates()
    {
        var sut = Service(intervalHours: 0);

        await sut.StartAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(48));
        await Task.Delay(50);
        await sut.StopAsync(CancellationToken.None);

        _consolidator.Verify(c => c.ConsolidateAllAsync(It.IsAny<CancellationToken>()), Times.Never,
            "interval 0 is the opt-in default off — no cycles run");
    }
}
