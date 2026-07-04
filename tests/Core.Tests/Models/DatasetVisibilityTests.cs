using Edda.Core.Models;

namespace Edda.Core.Tests.Models;

/// <summary>
/// Unit tests for <see cref="DatasetVisibility"/> (ADR-0014): the unrestricted default must expose an empty
/// visible set, and a restricted visibility must carry an ordinal, case-sensitive copy of the given ids.
/// </summary>
public class DatasetVisibilityTests
{
    [Fact]
    public void Unrestricted_IsUnrestricted_WithEmptyVisibleSet()
    {
        var visibility = DatasetVisibility.Unrestricted;

        visibility.IsUnrestricted.Should().BeTrue();
        visibility.VisibleDatasetIds.Should().BeEmpty();
    }

    [Fact]
    public void Unrestricted_IsASingleton()
        => DatasetVisibility.Unrestricted.Should().BeSameAs(DatasetVisibility.Unrestricted);

    [Fact]
    public void Restricted_IsNotUnrestricted_AndCarriesTheGivenIds()
    {
        var visibility = DatasetVisibility.Restricted(["git:a", "upload:b"]);

        visibility.IsUnrestricted.Should().BeFalse();
        visibility.VisibleDatasetIds.Should().BeEquivalentTo("git:a", "upload:b");
    }

    [Fact]
    public void Restricted_EmptyIds_YieldsEmptyVisibleSet()
    {
        var visibility = DatasetVisibility.Restricted([]);

        visibility.IsUnrestricted.Should().BeFalse();
        visibility.VisibleDatasetIds.Should().BeEmpty();
    }

    [Fact]
    public void Restricted_IsCaseSensitive_Ordinal()
    {
        var visibility = DatasetVisibility.Restricted(["git:Repo"]);

        visibility.VisibleDatasetIds.Should().Contain("git:Repo");
        visibility.VisibleDatasetIds.Should().NotContain("git:repo");
    }

    [Fact]
    public void Restricted_CopiesInput_DecouplingFromTheSourceCollection()
    {
        var source = new List<string> { "git:a" };

        var visibility = DatasetVisibility.Restricted(source);
        source.Add("git:b");

        visibility.VisibleDatasetIds.Should().BeEquivalentTo("git:a");
    }

    [Fact]
    public void Restricted_NullIds_Throws()
    {
        var act = () => DatasetVisibility.Restricted(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
