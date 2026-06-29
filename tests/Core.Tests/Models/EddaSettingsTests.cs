using Edda.Core.Models;

namespace Edda.Core.Tests.Models;

public sealed class EddaSettingsTests
{
    [Fact]
    public void EddaSettings_Default_HasSchemaVersionOneAndEmptyGeneral()
    {
        var settings = new EddaSettings();

        settings.SchemaVersion.Should().Be(1);
        settings.General.Should().NotBeNull();
        settings.General.EnableIngestion.Should().BeNull();
    }

    [Fact]
    public void GeneralSettings_Default_HasNullEnableIngestion()
        => new GeneralSettings().EnableIngestion.Should().BeNull();

    [Fact]
    public void EddaSettings_Default_HasEmptyLlmEnrichment()
    {
        var settings = new EddaSettings();

        settings.LlmEnrichment.Should().NotBeNull();
        settings.LlmEnrichment.Enabled.Should().BeNull();
        settings.LlmEnrichment.Provider.Should().BeNull();
    }
}
