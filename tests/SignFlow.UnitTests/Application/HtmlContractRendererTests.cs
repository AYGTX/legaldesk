using SignFlow.Application.Common.Services;
using SignFlow.Domain.Enums;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// The renderer decides what wording ends up in a contract, so these tests are the specification
/// for that behaviour rather than a smoke check.
/// </summary>
public class HtmlContractRendererTests
{
    private static Dictionary<string, HtmlTemplateFieldType> Types(
        params (string Key, HtmlTemplateFieldType Type)[] entries)
        => entries.ToDictionary(e => e.Key, e => e.Type);

    // ---------------------------------------------------------------- existing behaviour

    [Fact]
    public void Substitutes_placeholders_and_html_encodes_the_value()
    {
        var html = HtmlContractRenderer.Render(
            "<p>Party: {{party}}</p>",
            new Dictionary<string, string> { ["party"] = "Dupont & Fils <SARL>" });

        Assert.Equal("<p>Party: Dupont &amp; Fils &lt;SARL&gt;</p>", html);
    }

    [Fact]
    public void Strips_placeholders_that_were_never_filled()
    {
        var html = HtmlContractRenderer.Render(
            "<p>Signed: {{approverSignature}}</p>",
            new Dictionary<string, string>());

        Assert.Equal("<p>Signed: </p>", html);
    }

    /// <summary>
    /// The overwhelming majority of stored contracts contain no conditions at all. They must render
    /// exactly as they did before conditions existed.
    /// </summary>
    [Fact]
    public void Template_without_conditions_is_untouched_apart_from_substitution()
    {
        const string source = "<h1>Contrat</h1><p>Client {{client}}, montant {{amount}} EUR.</p>";

        var html = HtmlContractRenderer.Render(
            source,
            new Dictionary<string, string> { ["client"] = "ACME", ["amount"] = "1000" });

        Assert.Equal("<h1>Contrat</h1><p>Client ACME, montant 1000 EUR.</p>", html);
    }

    [Fact]
    public void Null_or_empty_snapshot_renders_as_empty()
    {
        Assert.Equal(string.Empty, HtmlContractRenderer.Render(null, null));
        Assert.Equal(string.Empty, HtmlContractRenderer.Render("", null));
    }

    // ---------------------------------------------------------------- numeric conditions

    [Theory]
    [InlineData("50001", true)]
    [InlineData("50000", false)]   // strictly greater
    [InlineData("49999", false)]
    public void Number_greater_than_includes_only_above_the_threshold(string value, bool expected)
    {
        var html = HtmlContractRenderer.Render(
            "{{#if amount > 50000}}<p>Indemnity</p>{{/if}}",
            new Dictionary<string, string> { ["amount"] = value },
            Types(("amount", HtmlTemplateFieldType.Number)));

        Assert.Equal(expected ? "<p>Indemnity</p>" : string.Empty, html);
    }

    [Theory]
    [InlineData(">=", "50000", true)]
    [InlineData("<", "50000", false)]
    [InlineData("<=", "50000", true)]
    [InlineData("==", "50000", true)]
    [InlineData("!=", "50000", false)]
    public void Number_supports_every_comparison_operator(string op, string threshold, bool expected)
    {
        var html = HtmlContractRenderer.Render(
            $"{{{{#if amount {op} {threshold}}}}}KEEP{{{{/if}}}}",
            new Dictionary<string, string> { ["amount"] = "50000" },
            Types(("amount", HtmlTemplateFieldType.Number)));

        Assert.Equal(expected ? "KEEP" : string.Empty, html);
    }

    /// <summary>A decimal must not be read differently on a French-locale machine.</summary>
    [Fact]
    public void Number_comparison_is_invariant_culture()
    {
        var html = HtmlContractRenderer.Render(
            "{{#if amount > 1000.5}}KEEP{{/if}}",
            new Dictionary<string, string> { ["amount"] = "1000.75" },
            Types(("amount", HtmlTemplateFieldType.Number)));

        Assert.Equal("KEEP", html);
    }

    // ---------------------------------------------------------------- other field types

    [Theory]
    [InlineData("2030-01-01", true)]
    [InlineData("2020-01-01", false)]
    public void Date_compares_chronologically(string value, bool expected)
    {
        var html = HtmlContractRenderer.Render(
            "{{#if renewal > 2025-06-01}}KEEP{{/if}}",
            new Dictionary<string, string> { ["renewal"] = value },
            Types(("renewal", HtmlTemplateFieldType.Date)));

        Assert.Equal(expected ? "KEEP" : string.Empty, html);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Checkbox_bare_condition_tests_whether_it_is_ticked(string value, bool expected)
    {
        var html = HtmlContractRenderer.Render(
            "{{#if isConsumer}}<p>Consumer rights</p>{{/if}}",
            new Dictionary<string, string> { ["isConsumer"] = value },
            Types(("isConsumer", HtmlTemplateFieldType.Checkbox)));

        Assert.Equal(expected ? "<p>Consumer rights</p>" : string.Empty, html);
    }

    [Fact]
    public void Text_equality_ignores_case_and_accepts_quoted_values()
    {
        var html = HtmlContractRenderer.Render(
            "{{#if jurisdiction == \"eu\"}}<p>GDPR</p>{{/if}}",
            new Dictionary<string, string> { ["jurisdiction"] = "EU" },
            Types(("jurisdiction", HtmlTemplateFieldType.Text)));

        Assert.Equal("<p>GDPR</p>", html);
    }

    /// <summary>Ordering prose is meaningless, so a relational operator on text is unsatisfiable.</summary>
    [Fact]
    public void Text_does_not_support_relational_operators()
    {
        var html = HtmlContractRenderer.Render(
            "{{#if jurisdiction > \"EU\"}}KEEP{{/if}}",
            new Dictionary<string, string> { ["jurisdiction"] = "FR" },
            Types(("jurisdiction", HtmlTemplateFieldType.Text)));

        Assert.Equal(string.Empty, html);
    }

    // ---------------------------------------------------------------- else / structure

    [Fact]
    public void Else_branch_is_kept_when_the_condition_fails()
    {
        const string source = "{{#if amount > 50000}}<p>Full</p>{{#else}}<p>Simplified</p>{{/if}}";
        var types = Types(("amount", HtmlTemplateFieldType.Number));

        Assert.Equal("<p>Full</p>", HtmlContractRenderer.Render(
            source, new Dictionary<string, string> { ["amount"] = "60000" }, types));

        Assert.Equal("<p>Simplified</p>", HtmlContractRenderer.Render(
            source, new Dictionary<string, string> { ["amount"] = "10000" }, types));
    }

    [Fact]
    public void Kept_branch_still_has_its_placeholders_substituted()
    {
        var html = HtmlContractRenderer.Render(
            "{{#if isConsumer}}<p>Consumer {{client}}</p>{{/if}}",
            new Dictionary<string, string> { ["isConsumer"] = "true", ["client"] = "ACME" },
            Types(("isConsumer", HtmlTemplateFieldType.Checkbox)));

        Assert.Equal("<p>Consumer ACME</p>", html);
    }

    [Fact]
    public void Consecutive_blocks_are_resolved_independently()
    {
        var html = HtmlContractRenderer.Render(
            "{{#if a}}A{{/if}}|{{#if b}}B{{/if}}",
            new Dictionary<string, string> { ["a"] = "true", ["b"] = "false" },
            Types(("a", HtmlTemplateFieldType.Checkbox), ("b", HtmlTemplateFieldType.Checkbox)));

        Assert.Equal("A|", html);
    }

    [Fact]
    public void Block_may_span_multiple_lines()
    {
        var html = HtmlContractRenderer.Render(
            "{{#if a}}\n  <p>Multi</p>\n{{/if}}",
            new Dictionary<string, string> { ["a"] = "true" },
            Types(("a", HtmlTemplateFieldType.Checkbox)));

        Assert.Equal("\n  <p>Multi</p>\n", html);
    }

    // ---------------------------------------------------------------- fail-closed behaviour

    [Theory]
    [InlineData("unknownKey > 10")]           // key not in the filled values
    [InlineData("amount > notanumber")]       // right side unparseable
    public void Unsatisfiable_conditions_drop_the_block(string condition)
    {
        var html = HtmlContractRenderer.Render(
            $"{{{{#if {condition}}}}}KEEP{{{{/if}}}}",
            new Dictionary<string, string> { ["amount"] = "100" },
            Types(("amount", HtmlTemplateFieldType.Number)));

        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void Value_that_will_not_parse_as_a_number_drops_the_block()
    {
        var html = HtmlContractRenderer.Render(
            "{{#if amount > 10}}KEEP{{/if}}",
            new Dictionary<string, string> { ["amount"] = "" },
            Types(("amount", HtmlTemplateFieldType.Number)));

        Assert.Equal(string.Empty, html);
    }

    /// <summary>
    /// A template deleted after the request was raised leaves no field types. The contract must
    /// still render rather than throw.
    /// </summary>
    [Fact]
    public void Missing_field_types_fall_back_to_text_equality()
    {
        var html = HtmlContractRenderer.Render(
            "{{#if jurisdiction == EU}}<p>GDPR</p>{{/if}}",
            new Dictionary<string, string> { ["jurisdiction"] = "EU" },
            fieldTypes: null);

        Assert.Equal("<p>GDPR</p>", html);
    }

    /// <summary>A broken marker must never reach the reader, but its wording is preserved.</summary>
    [Fact]
    public void Stray_conditional_markers_are_stripped_but_content_survives()
    {
        var html = HtmlContractRenderer.Render(
            "<p>Wording</p>{{/if}}",
            new Dictionary<string, string>());

        Assert.Equal("<p>Wording</p>", html);
    }

    // ---------------------------------------------------------------- the divergence regression

    /// <summary>
    /// The reason this renderer exists: the signing screen and the sealed PDF used to substitute
    /// independently. Both now call <see cref="HtmlContractRenderer.Render"/>, so identical inputs
    /// must produce identical bytes — otherwise a signer could be shown wording the PDF omits.
    /// </summary>
    [Fact]
    public void Both_render_paths_produce_identical_output_for_identical_input()
    {
        const string snapshot = """
            <h1>{{title}}</h1>
            {{#if amount > 50000}}<p>Indemnity ceiling applies.</p>{{#else}}<p>Standard terms.</p>{{/if}}
            <p>Signed by {{approverSignature}}</p>
            """;
        var filled = new Dictionary<string, string> { ["title"] = "Contrat DCWI", ["amount"] = "75000" };
        var types = Types(("amount", HtmlTemplateFieldType.Number));

        // What GetSigningSessionQuery renders for the signing screen.
        var onScreen = HtmlContractRenderer.Render(snapshot, filled, types);
        // What ApproveSigningCommand hands to the PDF generator.
        var inPdf = HtmlContractRenderer.Render(snapshot, filled, types);

        Assert.Equal(onScreen, inPdf);
        Assert.Contains("Indemnity ceiling applies.", onScreen);
        Assert.DoesNotContain("Standard terms.", onScreen);
        Assert.DoesNotContain("{{", onScreen);
    }
}
