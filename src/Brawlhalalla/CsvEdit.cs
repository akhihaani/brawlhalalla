using System.Text;

namespace Brawlhalalla;

public sealed record CsvCell(int Line, int Column, string Value);
public sealed record CsvChange(int Line, int Column, string OldValue, string NewValue);

/// <summary>
/// Whole-cell editing of Brawlhalla's CSV string tables.
///
/// Replacement is only ever applied to a complete cell, never a substring, and never to column 0 —
/// that column holds the lookup key, which is an internal identifier. Line endings and quoting are
/// preserved byte-for-byte, since the entry's own name is derived from its first line and the game
/// is sensitive to the exact layout.
/// </summary>
public static class CsvEdit
{
    /// <summary>
    /// Scans cells. <paramref name="transform"/> receives (line, column, value) and returns a
    /// replacement, or null to leave the cell alone. Line 0 (the table-name header) and column 0
    /// (the key column) are never offered for replacement.
    /// </summary>
    public static string Rewrite(string csv, Func<int, int, string, string?> transform, List<CsvChange> changes)
    {
        StringBuilder output = new(csv.Length);
        int copied = 0;
        int line = 0, column = 0, cellStart = 0;
        bool inQuotes = false;

        for (int i = 0; i <= csv.Length; i++)
        {
            bool atEnd = i == csv.Length;
            char c = atEnd ? '\0' : csv[i];

            if (!atEnd && c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (inQuotes) continue;

            bool isComma = !atEnd && c == ',';
            bool isNewline = !atEnd && (c == '\n' || c == '\r');
            if (!isComma && !isNewline && !atEnd) continue;

            // Close the current cell at [cellStart, i).
            string raw = csv[cellStart..i];
            bool editable = line > 0 && column > 0 && raw.Length > 0;
            if (editable)
            {
                bool quoted = raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"';
                string value = quoted ? raw[1..^1].Replace("\"\"", "\"") : raw;
                string? replacement = transform(line, column, value);

                if (replacement is not null && replacement != value)
                {
                    output.Append(csv, copied, cellStart - copied);
                    output.Append(EncodeCell(replacement, quoted));
                    copied = i;
                    changes.Add(new CsvChange(line, column, value, replacement));
                }
            }

            if (atEnd) break;

            if (isComma)
            {
                column++;
                cellStart = i + 1;
            }
            else
            {
                // Consume \r\n as a single break without disturbing the copied span.
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                line++;
                column = 0;
                cellStart = i + 1;
            }
        }

        if (changes.Count == 0) return csv;
        output.Append(csv, copied, csv.Length - copied);
        return output.ToString();
    }

    /// <summary>Reads every cell without modifying anything, for diagnostics.</summary>
    public static List<CsvCell> ReadCells(string csv)
    {
        List<CsvCell> cells = [];
        Rewrite(csv, (line, col, value) =>
        {
            cells.Add(new CsvCell(line, col, value));
            return null;
        }, []);
        return cells;
    }

    private static string EncodeCell(string value, bool wasQuoted)
    {
        bool needsQuotes = wasQuoted
            || value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuotes ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }
}
