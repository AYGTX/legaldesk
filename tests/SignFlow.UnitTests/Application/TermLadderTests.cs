using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.UnitTests.Application;

/// <summary>The alert ladder (R-M10-3), automatic renewal (R-M10-2), decisions (R-M10-4) and the date reader (R-M10-7).</summary>
public class TermLadderTests
{
    private static readonly Guid Person = Guid.NewGuid();

    private static ContractTerm Term(DateTime end, int? noticeDays = null, RenewalMode renewal = RenewalMode.None, int? renewalMonths = null,
        DateTime? start = null)
    {
        var term = ContractTerm.For(Guid.NewGuid());
        term.Set(start ?? end.AddYears(-1), end, renewal, renewalMonths, noticeDays, NoticeUnit.Days, null, Person);
        return term;
    }

    [Fact]
    public void Ninety_days_notice_on_31_December_first_alerts_sixty_days_before_2_October_not_on_it()
    {
        var term = Term(new DateTime(2026, 12, 31), noticeDays: 90);
        Assert.Equal(new DateTime(2026, 10, 2), term.NoticeDeadline);

        Assert.Null(TermLadder.Due(term, new DateTime(2026, 8, 2), TermLadder.DefaultOffsets));
        var first = TermLadder.Due(term, new DateTime(2026, 8, 3), TermLadder.DefaultOffsets);

        Assert.NotNull(first);
        Assert.Equal((new DateTime(2026, 10, 2), 60, false), (first!.Anchor, first.Offset, first.IsEnd));
        Assert.False(first.Escalates);
    }

    [Fact]
    public void Each_rung_is_sent_once_and_the_day_itself_escalates()
    {
        var term = Term(new DateTime(2026, 12, 31), noticeDays: 90);
        var sent = new List<(int, bool)>();
        for (var day = new DateTime(2026, 7, 1); day <= new DateTime(2027, 1, 5); day = day.AddDays(1))
        {
            if (TermLadder.Due(term, day, TermLadder.DefaultOffsets) is not { } rung) continue;
            sent.Add((rung.Offset, rung.IsEnd));
            if (rung.IsEnd) term.MarkEndAlertSent(); else term.MarkAlertSent(rung.Anchor, rung.Offset);
        }

        Assert.Equal([(60, false), (30, false), (14, false), (7, false), (0, false), (0, true)], sent);
    }

    [Fact]
    public void A_term_recorded_late_jumps_to_its_current_rung()
    {
        var term = Term(new DateTime(2026, 12, 31), noticeDays: 30);

        var rung = TermLadder.Due(term, new DateTime(2026, 11, 25), TermLadder.DefaultOffsets);

        Assert.Equal(7, rung!.Offset);
    }

    [Fact]
    public void A_recorded_decision_stops_the_ladder()
    {
        var term = Term(new DateTime(2026, 12, 31), noticeDays: 30);
        term.Decide(TermDecision.Renew, Person);

        Assert.Null(TermLadder.Due(term, new DateTime(2026, 11, 25), TermLadder.DefaultOffsets));
    }

    [Fact]
    public void An_automatic_renewal_with_no_decision_rolls_to_the_next_term_and_the_ladder_restarts()
    {
        var term = Term(new DateTime(2026, 12, 31), noticeDays: 30, RenewalMode.Automatic, renewalMonths: 12);
        term.MarkAlertSent(term.Anchor!.Value, 0);

        var rolled = term.RollForward(new DateTime(2027, 1, 1));

        Assert.Equal([new DateTime(2026, 12, 31)], rolled);
        Assert.Equal(new DateTime(2027, 12, 31), term.CurrentEndDate);
        Assert.Null(term.LastAlertOffset);
        Assert.Equal(60, TermLadder.Due(term, new DateTime(2027, 10, 2), TermLadder.DefaultOffsets)!.Offset);
    }

    [Fact]
    public void A_terminated_contract_does_not_roll_over()
    {
        var term = Term(new DateTime(2026, 12, 31), noticeDays: 30, RenewalMode.Automatic, renewalMonths: 12);
        term.Decide(TermDecision.Terminate, Person, new DateTime(2026, 11, 20), "notices/proof.pdf");

        Assert.Empty(term.RollForward(new DateTime(2027, 1, 2)));
        Assert.Equal(new DateTime(2026, 12, 31), term.CurrentEndDate);
    }

    [Fact]
    public void A_notice_period_in_months_counts_back_calendar_months()
    {
        var term = ContractTerm.For(Guid.NewGuid());
        term.Set(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), RenewalMode.None, null, 3, NoticeUnit.Months, NoticeForm.RegisteredMail, Person);

        Assert.Equal(new DateTime(2026, 9, 30), term.NoticeDeadline);
    }

    [Fact]
    public void The_organisations_offsets_always_include_the_day_itself()
    {
        Assert.Equal([90, 45, 10, 0], TermLadder.Offsets("10, 90,45"));
        Assert.Equal(TermLadder.DefaultOffsets, TermLadder.Offsets("nonsense"));
    }

    [Theory]
    [InlineData("03/04/2027", 2027, 4, 3)]
    [InlineData("3 avril 2027", 2027, 4, 3)]
    [InlineData("1er janvier 2027", 2027, 1, 1)]
    [InlineData("April 3, 2027", 2027, 4, 3)]
    [InlineData("3 April 2027", 2027, 4, 3)]
    [InlineData("2027-04-03", 2027, 4, 3)]
    [InlineData("3 أفريل 2027", 2027, 4, 3)]
    [InlineData("٣ يناير ٢٠٢٧", 2027, 1, 3)]
    public void Reads_a_date_as_written_in_a_contract(string text, int y, int m, int d)
    {
        Assert.Equal(new DateTime(y, m, d), ContractDates.Parse(text));
    }

    [Theory]
    [InlineData("04/31/2027")]  // no 31st month: month/day is never assumed
    [InlineData("next spring")]
    [InlineData("")]
    public void Does_not_guess_a_date(string text)
    {
        Assert.Null(ContractDates.Parse(text));
    }
}
