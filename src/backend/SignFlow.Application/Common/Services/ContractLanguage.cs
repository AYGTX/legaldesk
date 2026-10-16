using System.Text.RegularExpressions;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Which language a contract is written in, from its most common function words. Used to compare a contract only
/// with the regulatory texts written in that language: the registry holds each law once per language, and an
/// English entry's keywords can never match a French contract, so without this every law reports a false gap.
/// </summary>
public static class ContractLanguage
{
    private static readonly HashSet<string> French = ["le", "la", "les", "des", "du", "et", "est", "une", "pour", "dans", "par", "sur", "aux", "qui", "que", "sont", "ou", "au"];
    private static readonly HashSet<string> English = ["the", "and", "of", "to", "is", "for", "in", "shall", "by", "with", "this", "that", "be", "any", "or", "are", "on", "as"];
    private static readonly Regex Word = new(@"\p{L}+", RegexOptions.Compiled);
    private static readonly Regex ArabicLetter = new(@"\p{IsArabic}", RegexOptions.Compiled);

    /// <returns>"fr", "en", "ar", or null when the text is too short or too mixed to tell.</returns>
    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var sample = text.Length > 20_000 ? text[..20_000] : text;
        var letters = sample.Count(char.IsLetter);
        if (letters > 0 && ArabicLetter.Matches(sample).Count > letters / 2) return "ar";

        int fr = 0, en = 0;
        foreach (Match m in Word.Matches(sample.ToLowerInvariant()))
        {
            if (French.Contains(m.Value)) fr++;
            else if (English.Contains(m.Value)) en++;
        }

        if (fr + en < 10) return null;
        if (fr >= en * 2) return "fr";
        if (en >= fr * 2) return "en";
        return null;
    }

    /// <summary>The regulations in the contract's language; all of them when the language is unknown or none match.</summary>
    public static List<T> Filter<T>(IReadOnlyList<T> regulations, Func<T, string> languageOf, string? contractLanguage)
    {
        if (contractLanguage is null) return [.. regulations];
        var matching = regulations.Where(r => string.Equals(languageOf(r), contractLanguage, StringComparison.OrdinalIgnoreCase)).ToList();
        return matching.Count > 0 ? matching : [.. regulations];
    }
}
