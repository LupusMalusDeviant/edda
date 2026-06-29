using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Models;

/// <summary>Unit tests for <see cref="IngestionResult"/> and <see cref="IngestionError"/>.</summary>
public sealed class IngestionResultTests
{
    [Fact]
    public void Empty_HasZeroCountsAndNoErrors()
    {
        var result = IngestionResult.Empty;

        result.Imported.Should().Be(0);
        result.Updated.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(0);
        result.Errors.Should().BeEmpty();
        result.TotalProcessed.Should().Be(0);
    }

    [Fact]
    public void TotalProcessed_SumsAllOutcomeCounts()
    {
        var result = new IngestionResult { Imported = 2, Updated = 3, Skipped = 1, Failed = 4 };

        result.TotalProcessed.Should().Be(10);
    }

    [Fact]
    public void Errors_CarryOptionalItemContext()
    {
        var result = new IngestionResult
        {
            Failed = 1,
            Errors = [new IngestionError { ItemId = "git:repo:doc", Message = "clone failed" }]
        };

        result.Errors.Should().ContainSingle();
        result.Errors[0].ItemId.Should().Be("git:repo:doc");
        result.Errors[0].Message.Should().Be("clone failed");
    }

    [Fact]
    public void IngestionError_ItemId_IsOptional()
    {
        var error = new IngestionError { Message = "global failure" };

        error.ItemId.Should().BeNull();
        error.Message.Should().Be("global failure");
    }
}
