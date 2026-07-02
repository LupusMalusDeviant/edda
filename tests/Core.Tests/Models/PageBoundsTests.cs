using Edda.Core.Models;

namespace Edda.Core.Tests.Models;

/// <summary>
/// Unit tests for <see cref="PageBounds.Resolve(int?, int?, out string?)"/> covering the pagination
/// boundary conditions used by the list endpoints.
/// </summary>
public class PageBoundsTests
{
    [Fact]
    public void Resolve_NoParameters_ReturnsFullSetCappedAtMax()
    {
        var bounds = PageBounds.Resolve(skip: null, take: null, out var error);

        error.Should().BeNull();
        bounds!.Skip.Should().Be(0);
        bounds.Take.Should().Be(PageBounds.MaxTake);
    }

    [Fact]
    public void Resolve_SkipOnly_OptsIntoPaginationWithDefaultPageSize()
    {
        var bounds = PageBounds.Resolve(skip: 10, take: null, out var error);

        error.Should().BeNull();
        bounds!.Skip.Should().Be(10);
        bounds.Take.Should().Be(PageBounds.DefaultPageSize);
    }

    [Fact]
    public void Resolve_SkipAndTake_UsesProvidedValues()
    {
        var bounds = PageBounds.Resolve(skip: 5, take: 50, out var error);

        error.Should().BeNull();
        bounds!.Skip.Should().Be(5);
        bounds.Take.Should().Be(50);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(PageBounds.MaxTake)]
    public void Resolve_TakeWithinRange_IsValid(int take)
    {
        var bounds = PageBounds.Resolve(skip: 0, take: take, out var error);

        error.Should().BeNull();
        bounds!.Take.Should().Be(take);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Resolve_NegativeSkip_ReturnsError(int skip)
    {
        var bounds = PageBounds.Resolve(skip: skip, take: null, out var error);

        bounds.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PageBounds.MaxTake + 1)]
    [InlineData(5000)]
    public void Resolve_TakeOutOfRange_ReturnsError(int take)
    {
        var bounds = PageBounds.Resolve(skip: 0, take: take, out var error);

        bounds.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }
}
