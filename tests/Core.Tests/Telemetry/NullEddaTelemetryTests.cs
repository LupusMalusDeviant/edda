using Edda.Core.Telemetry;

namespace Edda.Core.Tests.Telemetry;

/// <summary>Unit tests for <see cref="NullEddaTelemetry"/> (D7): it records nothing and starts no span.</summary>
public sealed class NullEddaTelemetryTests
{
    [Fact]
    public void StartActivity_ReturnsNull()
        => NullEddaTelemetry.Instance.StartActivity("op").Should().BeNull();

    [Fact]
    public void RecordDuration_DoesNotThrow()
    {
        var act = () => NullEddaTelemetry.Instance.RecordDuration("op", 1.0, success: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void Instance_IsSingleton()
        => NullEddaTelemetry.Instance.Should().BeSameAs(NullEddaTelemetry.Instance);
}
