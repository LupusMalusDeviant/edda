namespace Edda.AKG.Chunking;

/// <summary>Shared line helpers for chunking that preserve original line endings.</summary>
internal static class LineUtilities
{
    /// <summary>
    /// Splits text into lines, keeping each line's trailing newline so that concatenating the returned
    /// lines reproduces the input exactly.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <returns>The lines, each including its trailing <c>\n</c> except possibly the last.</returns>
    public static List<string> SplitKeepEndings(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add(text[start..(i + 1)]);
                start = i + 1;
            }
        }

        if (start < text.Length)
            lines.Add(text[start..]);

        return lines;
    }
}
