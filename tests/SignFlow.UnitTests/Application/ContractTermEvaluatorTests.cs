using SignFlow.Application.Common.Services;
using SignFlow.Domain.Enums;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// These alerts tell a legal team their notice window has closed, so the boundaries are the
/// specification rather than a smoke check. Every case is anchored to a fixed "today" — a test that
/// depends on the real clock would pass or fail depending on the day it ran.
/// </summary>
public class ContractTermEvaluatorTests
{
    private static readonly DateTime Today = new(2026, 8, 14);

    private static ContractTermStatus Evaluate(
        DateTime? end,
        int? noticePeriodDays = null,
        DateTime? start = null)
        => ContractTermEvaluator.Evaluate(start, end, noticePeriodDays is > 0 && end is { } e ? e.AddDays(-noticePeriodDays.Value) : null, Today);

    // ---------------------------------------------------------------- lifecycle stages

    [Fact]
    public void Reports_unknown_when_no_end_date_is_recorded()
    {
        var status = Evaluate(end: null);

        Assert.Equal(LifecycleStage.Unknown, status.Stage);
        Assert.Null(status.NoticeDeadline);
        Assert.Null(status.DaysToEnd);
    }

    [Fact]
    public void Reports_not_started_before_the_term_begins()
    {
        var status = Evaluate(end: Today.AddYears(1), start: Today.AddDays(10));

        Assert.Equal(LifecycleStage.NotStarted, status.Stage);
    }

    [Fact]
    public void Reports_in_force_while_expiry_is_far_off()
    {
        var status = Evaluate(end: Today.AddDays(200));

        Assert.Equal(LifecycleStage.InForce, status.Stage);
        Assert.Equal(200, status.DaysToEnd);
    }

    [Theory]
    [InlineData(30)]   // boundary: exactly 30 days out still counts as expiring soon
    [InlineData(1)]
    [InlineData(0)]    // expires today — not yet expired
    public void Reports_expiring_soon_within_thirty_days(int daysToEnd)
    {
        var status = Evaluate(end: Today.AddDays(daysToEnd));

        Assert.Equal(LifecycleStage.ExpiringSoon, status.Stage);
    }

    [Fact]
    public void Reports_in_force_one_day_outside_the_expiring_soon_window()
    {
        var status = Evaluate(end: Today.AddDays(31));

        Assert.Equal(LifecycleStage.InForce, status.Stage);
    }

    [Fact]
    public void Reports_expired_the_day_after_the_end_date()
    {
        var status = Evaluate(end: Today.AddDays(-1));

        Assert.Equal(LifecycleStage.Expired, status.Stage);
        Assert.Equal(-1, status.DaysToEnd);
    }

    // ---------------------------------------------------------------- the notice deadline

    [Fact]
    public void Derives_the_notice_deadline_by_counting_back_from_the_end_date()
    {
        var status = Evaluate(end: new DateTime(2026, 12, 31), noticePeriodDays: 90);

        Assert.Equal(new DateTime(2026, 10, 2), status.NoticeDeadline);
    }

    [Fact]
    public void Has_no_notice_deadline_when_no_notice_period_is_recorded()
    {
        var status = Evaluate(end: Today.AddDays(45));

        Assert.Null(status.NoticeDeadline);
        Assert.Null(status.DaysToNoticeDeadline);
        Assert.Equal(LifecycleStage.InForce, status.Stage);
    }

    [Fact]
    public void Treats_a_zero_notice_period_as_no_notice_deadline()
    {
        var status = Evaluate(end: Today.AddDays(45), noticePeriodDays: 0);

        Assert.Null(status.NoticeDeadline);
        Assert.Equal(LifecycleStage.InForce, status.Stage);
    }

    [Fact]
    public void Flags_the_notice_deadline_the_day_it_arrives()
    {
        // End in 60 days with 60 days' notice: the deadline is today.
        var status = Evaluate(end: Today.AddDays(60), noticePeriodDays: 60);

        Assert.Equal(0, status.DaysToNoticeDeadline);
        Assert.Equal(LifecycleStage.NoticeDeadlinePassed, status.Stage);
    }

    [Fact]
    public void Notice_deadline_outranks_a_near_expiry()
    {
        // Both would apply: 20 days to expiry, and the notice deadline passed 10 days ago.
        var status = Evaluate(end: Today.AddDays(20), noticePeriodDays: 30);

        Assert.Equal(LifecycleStage.NoticeDeadlinePassed, status.Stage);
    }

    [Fact]
    public void An_expired_contract_is_expired_even_with_a_passed_notice_deadline()
    {
        var status = Evaluate(end: Today.AddDays(-5), noticePeriodDays: 30);

        Assert.Equal(LifecycleStage.Expired, status.Stage);
    }
}
