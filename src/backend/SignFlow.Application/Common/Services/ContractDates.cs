using System.Globalization;
using System.Text.RegularExpressions;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Reads a date written in a contract (R-M10-7): ISO (2027-04-03), day/month/year with / - or . (03/04/2027 is 3 April —
/// month/day is never assumed), and written dates with French, English or Arabic month names ("3 avril 2027",
/// "April 3, 2027", "3 أفريل 2027"). Anything else is not a date: the caller suggests nothing rather than guess.
/// </summary>
public static class ContractDates
{
    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        // French
        ["janvier"] = 1, ["février"] = 2, ["fevrier"] = 2, ["mars"] = 3, ["avril"] = 4, ["mai"] = 5, ["juin"] = 6,
        ["juillet"] = 7, ["août"] = 8, ["aout"] = 8, ["septembre"] = 9, ["octobre"] = 10, ["novembre"] = 11,
        ["décembre"] = 12, ["decembre"] = 12,
        // English
        ["january"] = 1, ["february"] = 2, ["march"] = 3, ["april"] = 4, ["may"] = 5, ["june"] = 6, ["july"] = 7,
        ["august"] = 8, ["september"] = 9, ["october"] = 10, ["november"] = 11, ["december"] = 12,
        ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["apr"] = 4, ["jun"] = 6, ["jul"] = 7, ["aug"] = 8, ["sep"] = 9,
        ["sept"] = 9, ["oct"] = 10, ["nov"] = 11, ["dec"] = 12,
        // Arabic: the Maghreb names (used in Tunisia) and the Mashreq ones
        ["جانفي"] = 1, ["فيفري"] = 2, ["مارس"] = 3, ["أفريل"] = 4, ["افريل"] = 4, ["ماي"] = 5, ["جوان"] = 6,
        ["جويلية"] = 7, ["أوت"] = 8, ["اوت"] = 8, ["سبتمبر"] = 9, ["أكتوبر"] = 10, ["اكتوبر"] = 10, ["نوفمبر"] = 11,
        ["ديسمبر"] = 12,
        ["يناير"] = 1, ["فبراير"] = 2, ["أبريل"] = 4, ["ابريل"] = 4, ["مايو"] = 5, ["يونيو"] = 6, ["يوليو"] = 7,
        ["أغسطس"] = 8, ["اغسطس"] = 8,
    };

    private static readonly Regex Iso = new(@"^(\d{4})-(\d{1,2})-(\d{1,2})$", RegexOptions.Compiled);
    private static readonly Regex DayMonthYear = new(@"^(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{4})$", RegexOptions.Compiled);
    private static readonly Regex DayNameYear = new(@"^(\d{1,2})(?:er|st|nd|rd|th)?\s+(\p{L}+)\.?,?\s+(\d{4})$", RegexOptions.Compiled);
    private static readonly Regex NameDayYear = new(@"^(\p{L}+)\.?\s+(\d{1,2})(?:st|nd|rd|th)?,?\s+(\d{4})$", RegexOptions.Compiled);

    public static DateTime? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = Regex.Replace(text.Trim(), @"\s+", " ");
        // Arabic-Indic digits read as their Western equivalents.
        s = new string(s.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c).ToArray());

        Match m;
        if ((m = Iso.Match(s)).Success) return Make(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
        if ((m = DayMonthYear.Match(s)).Success) return Make(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value);
        if ((m = DayNameYear.Match(s)).Success && Months.TryGetValue(m.Groups[2].Value, out var mon1))
            return Make(m.Groups[3].Value, mon1.ToString(CultureInfo.InvariantCulture), m.Groups[1].Value);
        if ((m = NameDayYear.Match(s)).Success && Months.TryGetValue(m.Groups[1].Value, out var mon2))
            return Make(m.Groups[3].Value, mon2.ToString(CultureInfo.InvariantCulture), m.Groups[2].Value);
        return null;
    }

    private static DateTime? Make(string year, string month, string day)
    {
        var y = int.Parse(year, CultureInfo.InvariantCulture);
        var mo = int.Parse(month, CultureInfo.InvariantCulture);
        var d = int.Parse(day, CultureInfo.InvariantCulture);
        if (y is < 1900 or > 2200 || mo is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return null;
        return new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc);
    }
}
