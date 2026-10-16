using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// Rules about the answers to a template's questions that hold whichever way the answers arrive — the request form, the
/// API, or a revision after a reviewer asked for changes.
/// </summary>
public class TemplateAnswerRulesTests
{
    private const string Options = "[\"Congé annuel\",\"Congé maladie\"]";

    private static (string Key, string Label, string Type, string? OptionsJson)[] Dropdown() =>
        [("leave_type", "Type de congé", "Dropdown", Options)];

    [Fact]
    public void A_listed_option_is_accepted()
    {
        TemplateAnswerRules.RequireListedOptions(Dropdown(), new Dictionary<string, string> { ["leave_type"] = "Congé maladie" });
    }

    [Fact]
    public void An_answer_outside_the_list_is_refused_and_the_choices_are_named()
    {
        var ex = Assert.Throws<ValidationException>(() => TemplateAnswerRules.RequireListedOptions(
            Dropdown(), new Dictionary<string, string> { ["leave_type"] = "Vacances" }));

        Assert.Contains("Type de congé", ex.Message);
        Assert.Contains("Congé annuel", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_answer_is_left_to_the_required_rule(string blank)
    {
        TemplateAnswerRules.RequireListedOptions(Dropdown(), new Dictionary<string, string> { ["leave_type"] = blank });
        TemplateAnswerRules.RequireListedOptions(Dropdown(), new Dictionary<string, string>());
    }

    [Fact]
    public void Only_dropdowns_are_checked_against_a_list()
    {
        (string, string, string, string?)[] fields = [("note", "Note", "Text", Options)];

        TemplateAnswerRules.RequireListedOptions(fields, new Dictionary<string, string> { ["note"] = "anything" });
    }

    [Fact]
    public void The_comparison_is_exact_so_a_near_miss_does_not_slip_through()
    {
        Assert.Throws<ValidationException>(() => TemplateAnswerRules.RequireListedOptions(
            Dropdown(), new Dictionary<string, string> { ["leave_type"] = "congé annuel" }));
    }

    [Fact]
    public void Unreadable_options_mean_an_empty_list()
    {
        Assert.Empty(TemplateAnswerRules.Options(null));
        Assert.Empty(TemplateAnswerRules.Options("not json"));
        Assert.Equal(["a", "b"], TemplateAnswerRules.Options("[\"a\",\"b\"]"));
    }

    [Fact]
    public void Request_level_sources_resolve_for_every_kind_of_template()
    {
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), TemplateAnswerRules.ResolveRequestSource("system.today", "T"));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", TemplateAnswerRules.ResolveRequestSource("system.now", "T")!);
        Assert.Equal("Contrat Contoso", TemplateAnswerRules.ResolveRequestSource("request.title", "Contrat Contoso"));
    }

    [Fact]
    public void Profile_sources_and_unknown_ones_are_not_request_level()
    {
        Assert.Null(TemplateAnswerRules.ResolveRequestSource("user.fullName", "T"));
        Assert.Null(TemplateAnswerRules.ResolveRequestSource(null, "T"));
        Assert.Null(TemplateAnswerRules.ResolveRequestSource("system.nonsense", "T"));
    }
}
