using System.Net;
using System.Text.RegularExpressions;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Turns a rendered HTML contract into the plain text a reviewer — or a language model — reads. Block elements become
/// line breaks so articles and clauses stay separate lines, tags disappear, entities are decoded (the renderer HTML-encodes
/// every answer), and runs of blank space are collapsed. Deliberately small: contract markup is headings, paragraphs,
/// lists and tables, not arbitrary web pages.
/// </summary>
public static class HtmlToText
{
    private static readonly Regex Hidden = new(
        @"<(script|style|head)\b[^>]*>.*?</\1\s*>|<!--.*?-->",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LineBreak = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BlockEnd = new(
        @"</(p|div|h[1-6]|tr|table|ul|ol|section|article|blockquote)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CellEnd = new(@"</t[dh]\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // A list item starts its own line (see ListItem), so its closing tag adds no break of its own.
    private static readonly Regex ListItem = new(@"<li\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t ]+", RegexOptions.Compiled);

    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var text = Hidden.Replace(html, string.Empty);
        text = LineBreak.Replace(text, "\n");
        text = ListItem.Replace(text, "\n- ");
        text = CellEnd.Replace(text, " | ");
        text = BlockEnd.Replace(text, "\n");
        text = AnyTag.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        var lines = text
            .Split('\n')
            .Select(l => Spaces.Replace(l, " ").Trim().TrimEnd('|').Trim())
            .ToList();

        // At most one blank line in a row, none at the ends.
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (line.Length == 0 && (result.Count == 0 || result[^1].Length == 0)) continue;
            result.Add(line);
        }
        while (result.Count > 0 && result[^1].Length == 0) result.RemoveAt(result.Count - 1);

        return string.Join("\n", result);
    }
}
