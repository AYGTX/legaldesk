using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;

namespace SignFlow.UnitTests.Application;

/// <summary>The seeded Tunisian calendar (R-M4-10): fixed holidays every year, religious ones for 2026–2028.</summary>
public class HolidayCalendarTests
{
    private static readonly List<OrganisationHoliday> Seeded =
        [.. OrganisationHoliday.TunisianFixed(), .. OrganisationHoliday.TunisianReligious2026To2028()];

    [Theory]
    [InlineData(2027, 3, 20)]   // Independence Day, any year
    [InlineData(2026, 5, 27)]   // Eid al-Adha 2026
    [InlineData(2028, 2, 28)]   // Eid al-Fitr 2028, second day
    public void A_seeded_holiday_is_not_a_working_day(int y, int m, int d) =>
        Assert.False(WorkingCalendar.IsWorkingDay(new DateOnly(y, m, d), Seeded));

    [Fact]
    public void Every_year_from_2026_to_2028_has_its_six_religious_days()
    {
        foreach (var year in new[] { 2026, 2027, 2028 })
            Assert.Equal(6, OrganisationHoliday.TunisianReligious2026To2028().Count(h => h.Date.Year == year && !h.EveryYear));
    }

    [Fact]
    public void A_deadline_counted_in_working_days_steps_over_eid()
    {
        // Opened Tuesday 26 May 2026, 10:00 Tunis: Wed 27 and Thu 28 are Eid al-Adha, so 2 working days end on Monday 1 June.
        var due = WorkingCalendar.DueAt(new DateTime(2026, 5, 26, 9, 0, 0, DateTimeKind.Utc), 2, "Africa/Tunis", Seeded);
        Assert.NotNull(due);
        Assert.Equal(new DateOnly(2026, 6, 1), DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(due!.Value, WorkingCalendar.Zone("Africa/Tunis"))));
    }
}
