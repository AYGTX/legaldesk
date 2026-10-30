using System.Globalization;
using System.Text.RegularExpressions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Splits a contract into clauses and names them ("Article 5 — Responsabilité") for Harvey's review, which reads a
/// contract clause by clause. LegalDesk has no rules of its own: every finding comes from Harvey (D1).
/// </summary>
public static class ClauseSplitter
{
    private sealed record Clause(string Heading, string Text, int? Number);

    /// <summary>The clauses as the review names them ("Article 5 — Responsabilité").</summary>
    public static IReadOnlyList<ClauseText> ClauseTexts(string? text) =>
        SplitClauses(text).Select(ToClause).Select(c => new ClauseText(c.Heading, c.Text)).ToList();

    // ------------------------------------------------------------------ helpers

    // A heading is "ARTICLE 6 - …" / "Article 6 : …": the number is followed by a separator. A reference such as
    // "l'Article 14 du présent Contrat" has none, so it never splits a clause.
    private static readonly Regex InlineHeading = new(
        @"(?<=\S)\s+(?=(?i:article|clause)\s+\d+\s*[-–—:]\s)", RegexOptions.Compiled);

    /// <summary>
    /// Clauses of a contract whatever the text extraction did with line breaks: PDF text often arrives as one line per
    /// page, so headings are also found mid-line.
    /// </summary>
    public static List<string> SplitClauses(string? text) =>
        ContractDiffer.SplitClauses(text is null ? null : InlineHeading.Replace(text, "\n\n"));

    // Case-sensitive on purpose: the title is the run of capitals ("PÉNALITÉS") before the first sentence ("Pour …").
    private static readonly Regex Heading = new(
        @"^\s*(?i:article|art\.|clause|section)\s*(\d+)\s*[-–—:.)]?\s*([\p{Lu}\p{N}\s'’&/,-]{2,60}?)(?=\s+\p{Lu}\p{Ll}|\s*$)",
        RegexOptions.Compiled);

    private static Clause ToClause(string text)
    {
        var m = Heading.Match(text);
        if (!m.Success) return new Clause(text.Length > 50 ? text[..50] + "…" : text, text, null);
        var title = m.Groups[2].Value.Trim();
        var label = $"Article {m.Groups[1].Value}" + (title.Length > 0 ? $" — {Capitalize(title)}" : "");
        return new Clause(label, text, int.Parse(m.Groups[1].Value));
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..].ToLower(CultureInfo.InvariantCulture);
}
