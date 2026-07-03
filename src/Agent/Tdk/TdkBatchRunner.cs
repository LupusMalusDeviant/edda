namespace Edda.Agent.Tdk;

/// <summary>Provides the embedded <c>batch_runner.py</c> source used by the F11 batch path.</summary>
internal static class TdkBatchRunner
{
    private const string ResourceName = "Edda.Agent.Tdk.batch_runner.py";

    /// <summary>The batch runner Python source, read once from the assembly manifest.</summary>
    public static string Source { get; } = Read();

    private static string Read()
    {
        var assembly = typeof(TdkBatchRunner).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded batch runner resource '{ResourceName}' not found in {assembly.FullName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
