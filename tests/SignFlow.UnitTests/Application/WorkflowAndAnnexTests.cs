using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.UnitTests.Application;

public class RenderForReviewTests
{
    private static readonly Dictionary<string, string> Labels = new() { ["amount"] = "Montant", ["party"] = "Partie" };

    [Fact]
    public void Placeholders_read_as_labelled_gaps_and_signatures_leave_no_trace()
    {
        var html = HtmlContractRenderer.RenderForReview("<p>{{party}} paie {{amount}}.</p><div>{{clientSignature}}</div>", Labels);

        Assert.Contains("«Partie» paie «Montant».", html);
        Assert.DoesNotContain("clientSignature", html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public void Every_conditional_branch_is_kept_so_no_clause_hides_from_the_review()
    {
        var html = HtmlContractRenderer.RenderForReview(
            "{{#if amount > 1000}}<p>Garantie bancaire.</p>{{#else}}<p>Pas de garantie.</p>{{/if}}", Labels);

        Assert.Contains("Garantie bancaire.", html);
        Assert.Contains("Pas de garantie.", html);
        Assert.DoesNotContain("#if", html);
        Assert.DoesNotContain("/if", html);
    }

    [Fact]
    public void A_condition_with_no_else_still_keeps_its_clause()
    {
        var html = HtmlContractRenderer.RenderForReview("{{#if amount}}<p>Clause pénale.</p>{{/if}}", Labels);

        Assert.Contains("Clause pénale.", html);
    }

    [Fact]
    public void Labels_are_html_encoded()
    {
        var html = HtmlContractRenderer.RenderForReview("{{x}}", new Dictionary<string, string> { ["x"] = "<b>&" });

        Assert.Equal("«&lt;b&gt;&amp;»", html);
    }
}

public class WorkflowStepConditionTests
{
    private static WorkflowPresetStep Step(string? key, string? op = null, string? value = null)
        => WorkflowPresetStep.Create(Guid.NewGuid(), 1, 1, "Director", PresetApproverKind.User, Guid.NewGuid(), null, null, key, op, value);

    private static readonly Dictionary<string, HtmlTemplateFieldType> Types = new()
    {
        ["amount"] = HtmlTemplateFieldType.Number,
        ["country"] = HtmlTemplateFieldType.Text,
        ["urgent"] = HtmlTemplateFieldType.Checkbox
    };

    private static bool Applies(WorkflowPresetStep step, params (string Key, string Value)[] answers)
        => WorkflowPresetResolver.StepApplies(step, answers.ToDictionary(a => a.Key, a => a.Value), Types);

    [Fact]
    public void A_step_with_no_condition_always_applies()
        => Assert.True(Applies(Step(null)));

    [Theory]
    [InlineData("12000", true)]
    [InlineData("10000", false)]
    [InlineData("500", false)]
    public void A_numeric_condition_compares_the_answer(string answer, bool expected)
        => Assert.Equal(expected, Applies(Step("amount", ">", "10000"), ("amount", answer)));

    [Fact]
    public void A_text_condition_compares_without_regard_to_case()
        => Assert.True(Applies(Step("country", "==", "tunisie"), ("country", "Tunisie")));

    [Fact]
    public void A_filled_in_condition_follows_a_checkbox()
    {
        var step = Step("urgent");
        Assert.True(Applies(step, ("urgent", "true")));
        Assert.False(Applies(step, ("urgent", "false")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_or_blank_answer_never_rules_a_step_out(string? answer)
    {
        // Dropping the Director's approval because nobody typed the amount would skip the very check the condition guards.
        var answers = answer is null ? [] : new[] { ("amount", answer) };
        Assert.True(Applies(Step("amount", ">", "10000"), answers));
    }

    [Fact]
    public void An_answer_that_is_not_a_number_does_not_apply_the_numeric_step_but_is_not_blank()
        // An unparseable number compares false, the same rule conditional clauses follow.
        => Assert.False(Applies(Step("amount", ">", "10000"), ("amount", "beaucoup")));
}

public class PdfClauseAnnexTests
{
    [Fact]
    public void Each_clause_becomes_a_titled_section_with_its_wording()
    {
        var html = PdfClauseAnnex.BuildHtml(
            [new PdfClauseAnnex.AnnexClause("Confidentialité", "<p>Les parties gardent le secret.</p>", "fr"),
             new PdfClauseAnnex.AnnexClause("Droit applicable", "<p>Droit tunisien.</p>", "fr")],
            new Dictionary<string, string>());

        var text = System.Net.WebUtility.HtmlDecode(html);
        Assert.Contains("ANNEXE", text);
        Assert.Contains("<h2>Confidentialité</h2>", text);
        Assert.Contains("Les parties gardent le secret.", text);
        Assert.Contains("<h2>Droit applicable</h2>", text);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "class=\"cd-section\"").Count);
    }

    [Fact]
    public void English_clauses_get_an_english_heading()
    {
        var html = PdfClauseAnnex.BuildHtml([new PdfClauseAnnex.AnnexClause("Governing law", "<p>x</p>", "en")], new Dictionary<string, string>());

        Assert.Contains("ANNEX", html);
        Assert.Contains("Additional clauses", html);
    }

    [Fact]
    public void A_clause_title_cannot_inject_markup()
    {
        var html = PdfClauseAnnex.BuildHtml([new PdfClauseAnnex.AnnexClause("<script>x</script>", "<p>x</p>", "fr")], new Dictionary<string, string>());

        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Placeholders_and_conditions_in_a_clause_are_resolved_against_the_request_answers()
    {
        var html = PdfClauseAnnex.BuildHtml(
            [new PdfClauseAnnex.AnnexClause("Pénalité", "<p>{{party}} doit {{#if amount > 100}}une pénalité{{#else}}rien{{/if}}.{{unknownKey}}</p>", "fr")],
            new Dictionary<string, string> { ["party"] = "Atlas", ["amount"] = "500" },
            new Dictionary<string, HtmlTemplateFieldType> { ["amount"] = HtmlTemplateFieldType.Number });

        Assert.Contains("Atlas doit une pénalité.", System.Net.WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("{{", html);
    }
}

public class PdfTemplateAnnexTests
{
    private static PdfTemplate NewTemplate()
        => PdfTemplate.Create("T", "p", "f.pdf", 1, 1, Guid.NewGuid());

    [Fact]
    public void Annex_clause_ids_round_trip_without_duplicates()
    {
        var template = NewTemplate();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        template.SetAnnexClauses([a, b, a]);

        Assert.Equal([a, b], template.AnnexClauseIds);
    }

    [Fact]
    public void Clearing_the_annex_leaves_nothing_stored()
    {
        var template = NewTemplate();
        template.SetAnnexClauses([Guid.NewGuid()]);

        template.SetAnnexClauses([]);

        Assert.Empty(template.AnnexClauseIds);
        Assert.Null(template.AnnexClauseIdsJson);
    }
}
