using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// The organisation's working days (R-M4-10): Saturday, Sunday and its holidays are off. A step due in N working days
/// is due at the end of the N-th working day after the day it opened, in the organisation's time zone.
/// </summary>
public static class WorkingCalendar
{
    public static TimeZoneInfo Zone(string? timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZoneId) ? "Africa/Tunis" : timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static bool IsWorkingDay(DateOnly day, IReadOnlyCollection<OrganisationHoliday> holidays) =>
        day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Any(h => h.Covers(day));

    /// <summary>The UTC instant a step opened at <paramref name="openedUtc"/> is due, or null without a delay.</summary>
    public static DateTime? DueAt(DateTime openedUtc, int? workingDays, string? timeZoneId, IReadOnlyCollection<OrganisationHoliday> holidays)
    {
        if (workingDays is not > 0) return null;

        var zone = Zone(timeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(openedUtc, DateTimeKind.Utc), zone);
        var day = DateOnly.FromDateTime(local);
        var counted = 0;
        // A year of holidays cannot hold this loop: at most a few hundred days are skipped.
        while (counted < workingDays)
        {
            day = day.AddDays(1);
            if (IsWorkingDay(day, holidays)) counted++;
        }

        var endOfDay = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(23, 59, 59)), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(endOfDay, zone);
    }
}
