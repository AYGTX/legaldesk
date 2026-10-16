using System.Text;
using System.Text.RegularExpressions;

namespace SignFlow.Application.Common.Services;

/// <summary>One article found in pasted law text.</summary>
public record SplitArticle(string Number, string Text);

/// <summary>
/// What the splitter found. <see cref="Preamble"/> is everything before the first article (title, visas, recitals) —
/// shown so nothing is silently dropped, but not stored as an article. <see cref="Warnings"/> are things a person
/// should look at before trusting the split: duplicates, gaps in the numbering, articles with no text.
/// </summary>
public record ArticleSplit(string Preamble, List<SplitArticle> Articles, List<string> Warnings);

/// <summary>
/// Cuts the text of a law into its articles. Laws in this jurisdiction are published in French and Arabic, with
/// headings like "Article 12 -", "Art. 12 (nouveau) :", "Article premier" and "الفصل 12" / "الفصل الأول". The splitter
/// recognises those at the start of a line; everything up to the next heading is that article's text.
///
/// It is a convenience for entering a law, not a source of truth: the result is shown to a person who corrects it
/// and signs off the final articles against the official text.
/// </summary>
public static class LegalArticleSplitter
{
    // A heading is "Article <number>" (or the Arabic equivalents) at the start of a line, followed by nothing, by
    // punctuation, or by a bracket such as "(nouveau)". "Article 5 de la présente loi s'applique…" starts a
    // sentence, not an article, so a number followed by running words is not a heading.
    private static readonly Regex Heading = new(
        @"^\s*(?:article|art\.?|الفصل|فصل|المادة)\s+" +
        @"(?<num>premier|première|1er|1ère|الأول|الاول|[\d٠-٩]+(?:\s*(?:bis|ter|quater|quinquies|sexies|مكرر|ثالثا|رابعا))?(?:\s*[-–]\s*\d+)?)" +
        @"\s*(?<rest>$|[\.\-–—:\)\(،].*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Blank = new(@"\n{3,}", RegexOptions.Compiled);

    public static ArticleSplit Split(string? raw)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return new ArticleSplit(string.Empty, [], ["There is no text to split."]);

        var preamble = new StringBuilder();
        var articles = new List<(string Number, StringBuilder Text)>();

        foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var m = Heading.Match(line);
            if (m.Success)
            {
                var number = NormaliseNumber(m.Groups["num"].Value);
                var rest = m.Groups["rest"].Value.TrimStart(' ', '-', '–', '—', ':', '.');
                var text = new StringBuilder();
                if (rest.Length > 0) text.Append(rest.Trim()).Append('\n');
                articles.Add((number, text));
            }
            else if (articles.Count == 0)
            {
                preamble.Append(line).Append('\n');
            }
            else
            {
                articles[^1].Text.Append(line).Append('\n');
            }
        }

        var result = articles
            .Select(a => new SplitArticle(a.Number, Blank.Replace(a.Text.ToString().Trim(), "\n\n")))
            .ToList();

        if (result.Count == 0)
            warnings.Add("No article headings were found. Check that each article starts on its own line, like \"Article 1 -\" or \"الفصل 1\".");

        warnings.AddRange(CheckNumbering(result));
        foreach (var empty in result.Where(a => a.Text.Length == 0))
            warnings.Add($"Article {empty.Number} has no text.");

        return new ArticleSplit(preamble.ToString().Trim(), result, warnings);
    }

    /// <summary>"premier"/"1er"/"الأول" → "1"; Arabic-Indic digits → 0-9; "12  BIS" → "12 bis"; "12 – 3" → "12-3".</summary>
    public static string NormaliseNumber(string raw)
    {
        var n = raw.Trim().ToLowerInvariant();
        if (n is "premier" or "première" or "1er" or "1ère" or "الأول" or "الاول") return "1";

        var digits = new StringBuilder();
        foreach (var c in n)
            digits.Append(c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c);

        return Regex.Replace(digits.ToString(), @"\s*[-–]\s*", "-").Replace("  ", " ").Trim();
    }

    /// <summary>
    /// Plain numbers should count up by one. A repeat, a jump or a step backwards usually means the text was cut
    /// wrongly or a heading was missed — worth a look, never silently "fixed".
    /// </summary>
    private static IEnumerable<string> CheckNumbering(List<SplitArticle> articles)
    {
        var seen = new HashSet<string>();
        int? previous = null;

        foreach (var a in articles)
        {
            if (!seen.Add(a.Number))
                yield return $"Article {a.Number} appears more than once.";

            // Only plain integers take part in the sequence check; "12 bis" and "3-1" sit between their neighbours.
            if (!int.TryParse(a.Number, out var current)) continue;

            if (previous is { } p)
            {
                if (current > p + 1)
                    yield return $"Articles {p + 1}–{current - 1} are missing between article {p} and article {current}.";
                else if (current < p)
                    yield return $"Article {current} comes after article {p}: the order looks wrong.";
                // current == p is a repeat, already reported above
            }
            previous = current;
        }
    }
}
