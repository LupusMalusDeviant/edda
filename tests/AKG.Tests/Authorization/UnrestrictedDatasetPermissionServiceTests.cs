using Edda.AKG.Authorization;

namespace Edda.AKG.Tests.Authorization;

/// <summary>
/// Unit tests for <see cref="UnrestrictedDatasetPermissionService"/> (ADR-0014): the permissive default always
/// resolves to unrestricted visibility, which keeps rule reads behaviour-neutral.
/// </summary>
public class UnrestrictedDatasetPermissionServiceTests
{
    [Fact]
    public async Task ResolveVisibilityAsync_AlwaysUnrestricted()
    {
        var service = new UnrestrictedDatasetPermissionService();

        (await service.ResolveVisibilityAsync()).IsUnrestricted.Should().BeTrue();
    }
}
