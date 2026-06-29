using System.Text;

namespace Edda.AKG.Chunking;

/// <summary>
/// Splits an oversized pipe-table block into pieces that each stay within the size budget, repeating the
/// table header (and its separator row, if present) at the top of every piece so each chunk is a
/// self-contained, valid table. A single row that already exceeds the budget is emitted intact.
/// </summary>
internal static class TableSplitter
{
    /// <summary>Splits a table block by rows, repeating the header in each piece.</summary>
    /// <param name="text">The table block text.</param>
    /// <param name="maxChars">The maximum chunk size in characters.</param>
    /// <returns>The table pieces, each prefixed with the repeated header.</returns>
    public static List<string> Split(string text, int maxChars)
    {
        var lines = LineUtilities.SplitKeepEndings(text);
        if (lines.Count == 0)
            return [];

        var headerCount = lines.Count > 1 && IsSeparatorRow(lines[1]) ? 2 : 1;
        var header = string.Concat(lines.Take(headerCount));
        var bodyRows = lines.Skip(headerCount).ToList();
        if (bodyRows.Count == 0)
            return [text];

        var pieces = new List<string>();
        var current = new StringBuilder(header);
        foreach (var row in bodyRows)
        {
            if (current.Length > header.Length && current.Length + row.Length > maxChars)
            {
                pieces.Add(current.ToString());
                current.Clear();
                current.Append(header);
            }

            current.Append(row);
        }

        if (current.Length > header.Length)
            pieces.Add(current.ToString());

        return pieces.Count == 0 ? [text] : pieces;
    }

    private static bool IsSeparatorRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
            return false;

        var hasDash = false;
        foreach (var c in trimmed)
        {
            if (c == '-')
                hasDash = true;
            else if (c is not ('|' or ':' or ' '))
                return false;
        }

        return hasDash;
    }
}
