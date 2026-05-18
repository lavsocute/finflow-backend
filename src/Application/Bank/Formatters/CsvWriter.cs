using System.Text;

namespace FinFlow.Application.Bank.Formatters;

/// <summary>
/// Minimal CSV writer that handles escaping per RFC 4180. Used by all bank format
/// adapters so they only have to declare separator + headers + field order.
/// </summary>
internal static class CsvWriter
{
    public const char BomMarker = '\uFEFF';

    public static string Build(string separator, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, bool withBom = true)
    {
        var sb = new StringBuilder();
        if (withBom)
            sb.Append(BomMarker);

        AppendRow(sb, separator, headers);
        foreach (var row in rows)
            AppendRow(sb, separator, row);

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string separator, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
                sb.Append(separator);
            sb.Append(Escape(fields[i], separator));
        }
        sb.Append("\r\n");
    }

    private static string Escape(string value, string separator)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuote = value.Contains(separator)
            || value.Contains('"')
            || value.Contains('\n')
            || value.Contains('\r');

        if (!needsQuote)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
