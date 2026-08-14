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
        => ContractTermEvaluator.Evaluate(start, end, noticePeriodDays, Today);

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

    // ---------------------------------------------------------------- the alert ladder

    [Theory]
    [InlineData(90, ContractTermEvaluator.StageT90)]
    [InlineData(75, ContractTermEvaluator.StageT90)]
    [InlineData(60, ContractTermEvaluator.StageT60)]
    [InlineData(31, ContractTermEvaluator.StageT60)]   // one day short of the T30 rung
    [InlineData(30, ContractTermEvaluator.StageT30)]
    [InlineData(7, ContractTermEvaluator.StageT7)]
    [InlineData(0, ContractTermEvaluator.StageT7)]
    [InlineData(-1, ContractTermEvaluator.StageExpired)]
    public void Selects_the_ladder_rung_matching_the_days_remaining(int daysToEnd, string expected)
    {
        var status = Evaluate(end: Today.AddDays(daysToEnd));

        Assert.Equal(expected, ContractTermEvaluator.NextAlertStage(status, lastSentStage: null));
    }

    [Fact]
    public void Raises_nothing_while_expiry_is_beyond_the_widest_rung()
    {
        var status = Evaluate(end: Today.AddDays(91));

        Assert.Null(ContractTermEvaluator.NextAlertStage(status, lastSentStage: null));
    }

    [Fact]
    public void Raises_nothing_for_a_contract_whose_term_has_not_begun()
    {
        var status = Evaluate(end: Today.AddDays(20), start: Today.AddDays(5));

        Assert.Null(ContractTermEvaluator.NextAlertStage(status, lastSentStage: null));
    }

    [Fact]
    public void Raises_nothing_when_no_term_is_recorded()
    {
        var status = Evaluate(end: null);

        Assert.Null(ContractTermEvaluator.NextAlertStage(status, lastSentStage: null));
    }

    [Fact]
    public void Does_not_repeat_a_rung_already_sent()
    {
        var status = Evaluate(end: Today.AddDays(45));

        Assert.Null(ContractTermEvaluator.NextAlertStage(status, ContractTermEvaluator.StageT60));
    }

    [Fact]
    public void Advances_to_the_next_rung_once_the_contract_moves_on()
    {
        var status = Evaluate(end: Today.AddDays(25));

        Assert.Equal(
            ContractTermEvaluator.StageT30,
            ContractTermEvaluator.NextAlertStage(status, ContractTermEvaluator.StageT60));
    }

    /// <summary>
    /// A term recorded three weeks before expiry must not replay T90, T60 and T30 in one sweep —
    /// six notifications on day one would train the reader to ignore all of them.
    /// </summary>
    [Fact]
    public void A_term_recorded_late_starts_at_its_current_rung_only()
    {
        var status = Evaluate(end: Today.AddDays(21));

        var stage = ContractTermEvaluator.NextAlertStage(status, lastSentStage: null);

        Assert.Equal(ContractTermEvaluator.StageT30, stage);
        // And the skipped rungs never arrive afterwards.
        Assert.Null(ContractTermEvaluator.NextAlertStage(status, stage));
    }

    [Fact]
    public void The_notice_deadline_rung_fires_after_the_countdown_rungs()
    {
        var status = Evaluate(end: Today.AddDays(20), noticePeriodDays: 30);

        Assert.Equal(
            ContractTermEvaluator.StageNoticeDeadline,
            ContractTermEvaluator.NextAlertStage(status, ContractTermEvaluator.StageT30));
    }

    [Fact]
    public void Expiry_still_fires_after_the_notice_deadline_rung()
    {
        var status = Evaluate(end: Today.AddDays(-2), noticePeriodDays: 30);

        Assert.Equal(
            ContractTermEvaluator.StageExpired,
            ContractTermEvaluator.NextAlertStage(status, ContractTermEvaluator.StageNoticeDeadline));
    }

    [Fact]
    public void Expiry_is_the_final_rung()
    {
        var status = Evaluate(end: Today.AddDays(-40));

        Assert.Null(ContractTermEvaluator.NextAlertStage(status, ContractTermEvaluator.StageExpired));
    }

    /// <summary>
    /// A stage name persisted by an older release that no longer exists must not silently suppress
    /// every future alert for that contract.
    /// </summary>
    [Fact]
    public void An_unrecognised_stored_stage_does_not_mute_the_contract()
    {
        var status = Evaluate(end: Today.AddDays(20));

        Assert.Equal(
            ContractTermEvaluator.StageT30,
            ContractTermEvaluator.NextAlertStage(status, "SomeRetiredStage"));
    }

    // ---------------------------------------------------------------- escalation

    [Fact]
    public void Escalates_only_once_the_organisation_can_no_longer_act()
    {
        Assert.True(ContractTermEvaluator.IsEscalationStage(ContractTermEvaluator.StageNoticeDeadline));
        Assert.True(ContractTermEvaluator.IsEscalationStage(ContractTermEvaluator.StageExpired));

        Assert.False(ContractTermEvaluator.IsEscalationStage(ContractTermEvaluator.StageT90));
        Assert.False(ContractTermEvaluator.IsEscalationStage(ContractTermEvaluator.StageT60));
        Assert.False(ContractTermEvaluator.IsEscalationStage(ContractTermEvaluator.StageT30));
        Assert.False(ContractTermEvaluator.IsEscalationStage(ContractTermEvaluator.StageT7));
    }

    // ---------------------------------------------------------------- clock hygiene

    [Fact]
    public void Ignores_the_time_of_day_the_sweep_happens_to_run()
    {
        var end = new DateTime(2026, 9, 13);

        var earlyRun = ContractTermEvaluator.Evaluate(null, end, null, new DateTime(2026, 8, 14, 0, 1, 0));
        var lateRun = ContractTermEvaluator.Evaluate(null, end, null, new DateTime(2026, 8, 14, 23, 59, 0));

        Assert.Equal(earlyRun.DaysToEnd, lateRun.DaysToEnd);
        Assert.Equal(earlyRun.Stage, lateRun.Stage);
    }
}
