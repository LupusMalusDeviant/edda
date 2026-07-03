using Edda.Core.Abstractions;

namespace Edda.Agent.Tdk;

/// <summary>
/// <see cref="ITdkHelperModule"/> backed by the embedded <c>tdk.py</c> resource in this assembly.
/// The source is read once from the assembly manifest and cached for the process lifetime.
/// </summary>
internal sealed class TdkHelperModule : ITdkHelperModule
{
    private const string ResourceName = "Edda.Agent.Tdk.tdk.py";
    private static readonly string CachedSource = ReadResource();

    /// <inheritdoc />
    public string FileName => "tdk.py";

    /// <inheritdoc />
    public string Source => CachedSource;

    private static string ReadResource()
    {
        var assembly = typeof(TdkHelperModule).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded TDK helper resource '{ResourceName}' not found in {assembly.FullName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
