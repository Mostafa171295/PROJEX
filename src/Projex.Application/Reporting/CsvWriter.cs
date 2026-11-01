using System.Globalization;
using System.Text;

namespace Projex.Application.Reporting;

/// <summary>
/// RFC 4180 CSV with spreadsheet formula-injection protection: a text cell that starts with = + - @ (or tab/CR) and is not
/// a plain number is prefixed with an apostrophe so Excel/Sheets never evaluates it.
/// </summary>
public static class CsvWriter
{
    public static string Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        AppendRow(sb, headers);
        foreach (var row in rows)
        {
            if (row.Count != headers.Count) throw new ArgumentException("Every row must have the same number of cells as the header.");
            AppendRow(sb, row);
        }
        return sb.ToString();
    }

    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' &&
            !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string?> cells)
    {
        for (var i = 0; i < cells.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Cell(cells[i])); }
        sb.Append("\r\n");
    }

    public static string Num(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
