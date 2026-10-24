using System.Globalization;
using System.Text;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Checks that a quotation really is in a text. A language model's quote is only worth showing if it can be found
/// word for word, so a made-up or paraphrased "quote" is caught instead of being presented as the contract's wording.
/// Differences that PDF text extraction introduces (line breaks, repeated spaces, curly quotes, accents) are ignored;
/// anything else must match.
/// </summary>
public static class QuoteMatcher
{
    private const int MinQuoteChars = 12;

    public static bool IsVerbatim(string contractText, string quote)
    {
        var needle = Normalize(quote);
        return needle.Length >= MinQuoteChars && Normalize(contractText).Contains(needle, StringComparison.Ordinal);
    }

    private static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var ch = c switch
            {
                '’' or '‘' or '`' or '´' => '\'',
                '“' or '”' or '«' or '»' => '"',
                '–' or '—' => '-',
                _ => c
            };
            if (char.IsWhiteSpace(ch)) { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
