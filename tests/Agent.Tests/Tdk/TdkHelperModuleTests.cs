using Edda.Agent.Tdk;

namespace Edda.Agent.Tests.Tdk;

public class TdkHelperModuleTests
{
    private readonly TdkHelperModule _sut = new();

    [Fact]
    public void FileName_Always_ReturnsTdkPy()
    {
        _sut.FileName.Should().Be("tdk.py");
    }

    [Fact]
    public void Source_Always_IsNonEmpty()
    {
        _sut.Source.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Source_Always_ExposesPublicApi()
    {
        // The embedded helper must ship the documented surface: the decorator, the violation builder,
        // the AST helper and the atexit-driven JSON-I/O runner.
        _sut.Source.Should().Contain("def validator");
        _sut.Source.Should().Contain("def violation");
        _sut.Source.Should().Contain("def python_ast");
        _sut.Source.Should().Contain("atexit");
    }

    [Fact]
    public void Source_Always_IsStableAcrossInstances()
    {
        // Cached once from the manifest — every instance returns the identical source.
        new TdkHelperModule().Source.Should().Be(_sut.Source);
    }
}
