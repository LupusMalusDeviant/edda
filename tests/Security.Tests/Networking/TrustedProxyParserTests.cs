using System.Net;
using Edda.Security.Networking;

namespace Edda.Security.Tests.Networking;

/// <summary>Unit tests for <see cref="TrustedProxyParser.Parse(string?)"/>.</summary>
public class TrustedProxyParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespace_ReturnsEmpty(string? value)
        => TrustedProxyParser.Parse(value).Should().BeEmpty();

    [Fact]
    public void Parse_SingleIpv4_ReturnsOneAddress()
        => TrustedProxyParser.Parse("10.0.0.1").Should().Equal(IPAddress.Parse("10.0.0.1"));

    [Fact]
    public void Parse_CommaSeparated_ReturnsAllInOrder()
        => TrustedProxyParser.Parse("10.0.0.1,10.0.0.2,192.168.1.5")
            .Should().Equal(
                IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2"), IPAddress.Parse("192.168.1.5"));

    [Fact]
    public void Parse_SemicolonSeparatedWithWhitespace_TrimsAndParses()
        => TrustedProxyParser.Parse("  10.0.0.1 ; 10.0.0.2  ")
            .Should().Equal(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2"));

    [Fact]
    public void Parse_Ipv6Addresses_AreParsed()
        => TrustedProxyParser.Parse("::1, 2001:db8::1")
            .Should().Equal(IPAddress.Parse("::1"), IPAddress.Parse("2001:db8::1"));

    [Fact]
    public void Parse_InvalidEntries_AreSkipped()
        => TrustedProxyParser.Parse("10.0.0.1, not-an-ip, 999.1.1.1, 10.0.0.2")
            .Should().Equal(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2"));

    [Fact]
    public void Parse_Duplicates_AreCollapsedPreservingFirstSeenOrder()
        => TrustedProxyParser.Parse("10.0.0.2, 10.0.0.1, 10.0.0.2")
            .Should().Equal(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.1"));

    [Fact]
    public void Parse_AllInvalid_ReturnsEmpty()
        => TrustedProxyParser.Parse("garbage, nope, 300.300.300.300").Should().BeEmpty();
}
