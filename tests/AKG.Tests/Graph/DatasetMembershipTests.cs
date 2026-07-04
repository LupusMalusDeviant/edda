using Edda.AKG.Graph;

namespace Edda.AKG.Tests.Graph;

/// <summary>
/// Unit tests for <see cref="DatasetMembership"/> (ADR-0014): a rule's dataset id is the two-segment
/// <c>git:</c>/<c>upload:</c> head; anything else (hand-authored rules, structural roots, bare prefixes) has none.
/// </summary>
public class DatasetMembershipTests
{
    [Theory]
    [InlineData("git:my-repo:docs/adr/0001", "git:my-repo")]
    [InlineData("git:my-repo", "git:my-repo")]
    [InlineData("upload:handbook:intro", "upload:handbook")]
    [InlineData("upload:handbook", "upload:handbook")]
    [InlineData("git:my-repo:a/b/c", "git:my-repo")]
    [InlineData("git:repo:docs:adr:0001", "git:repo")]
    public void DatasetIdOf_ProvenanceRule_ReturnsHeadId(string ruleId, string expected)
        => DatasetMembership.DatasetIdOf(ruleId).Should().Be(expected);

    [Theory]
    [InlineData("use-async-await")]
    [InlineData("git-knowledge")]
    [InlineData("uploads")]
    [InlineData("git:")]
    [InlineData("upload:")]
    [InlineData("")]
    public void DatasetIdOf_NonProvenanceRule_ReturnsNull(string ruleId)
        => DatasetMembership.DatasetIdOf(ruleId).Should().BeNull();

    [Fact]
    public void DatasetIdOf_Null_ReturnsNull()
        => DatasetMembership.DatasetIdOf(null!).Should().BeNull();
}
