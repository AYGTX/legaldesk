using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Documents.Commands;

namespace SignFlow.UnitTests.Application;

/// <summary>Key data read by Harvey and the facts given to the renewal note.</summary>
public class ContractLifecycleAiTests
{
    [Fact]
    public void Verified_values_become_the_suggested_term()
    {
        var suggested = ExtractKeyTermsCommandHandler.Suggest(
        [
            new KeyTermDto("start_date", "2026-01-01", "q", true),
            new KeyTermDto("end_date", "2027-12-31", "q", true),
            new KeyTermDto("auto_renewal", "true", "q", true),
            new KeyTermDto("notice_period_days", "90", "q", true),
        ]);

        Assert.Equal(new DateTime(2027, 12, 31), suggested.EndDate);
        Assert.Equal(new DateTime(2026, 1, 1), suggested.StartDate);
        Assert.True(suggested.AutoRenews);
        Assert.Equal(90, suggested.NoticePeriodDays);
    }

    [Fact]
    public void Unreadable_values_are_left_empty()
    {
        var suggested = ExtractKeyTermsCommandHandler.Suggest(
        [
            new KeyTermDto("end_date", "fin 2027", "q", true),
            new KeyTermDto("notice_period_days", "trois mois", "q", true),
        ]);

        Assert.Null(suggested.EndDate);
        Assert.Null(suggested.NoticePeriodDays);
        Assert.Null(suggested.AutoRenews);
    }
}
