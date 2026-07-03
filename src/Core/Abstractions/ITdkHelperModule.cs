namespace Edda.Core.Abstractions;

/// <summary>
/// Provides the bundled Python <c>tdk</c> helper module that is placed next to a TDK validator
/// script inside the sandbox, letting validators import shared JSON-I/O, a <c>violation()</c>
/// builder and an AST helper instead of re-implementing them. Raw stdin/stdout scripts that never
/// import the module keep working unchanged.
/// </summary>
public interface ITdkHelperModule
{
    /// <summary>File name the helper is exposed under inside the sandbox (e.g. <c>tdk.py</c>).</summary>
    string FileName { get; }

    /// <summary>Full Python source of the helper module.</summary>
    string Source { get; }
}
