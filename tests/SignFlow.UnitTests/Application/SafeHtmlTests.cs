using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>The allow-list for template and clause bodies (R-M6-3, R-M9-7): what a reader's browser and the PDF renderer
/// are given never runs code and never fetches anything.</summary>
public class SafeHtmlTests
{
    [Fact]
    public void An_event_handler_is_stripped()
    {
        var clean = SafeHtml.Clean("<p>Prix</p><img src=x onerror=alert(1)>");

        Assert.DoesNotContain("onerror", clean);
        Assert.DoesNotContain("<img", clean);
        Assert.Contains("<p>Prix</p>", clean);
    }

    [Theory]
    [InlineData("<script>alert(1)</script><p>ok</p>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe><p>ok</p>")]
    [InlineData("<p style=\"background:url(http://169.254.169.254/)\">ok</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">ok</a>")]
    [InlineData("<img src=\"http://169.254.169.254/\"><p>ok</p>")]
    [InlineData("<style>@import url(http://evil.example/x.css)</style><p>ok</p>")]
    public void Nothing_that_runs_or_fetches_survives(string html)
    {
        var clean = SafeHtml.Clean(html);

        foreach (var bad in new[] { "<script", "alert(", "<iframe", "url(", "javascript:", "<img", "<style", "169.254", "evil" })
            Assert.DoesNotContain(bad, clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ok", clean);
    }

    [Fact]
    public void Contract_structure_placeholders_and_conditions_are_kept()
    {
        const string html =
            "<h2 style=\"text-align:center\">Article 1</h2>" +
            "<p class=\"clause\">Le client <strong>{{client_name}}</strong> paie {{amount}} TND.</p>" +
            "{{#if amount > 1000}}<p>Garantie bancaire.</p>{{/if}}" +
            "<ul><li>un</li></ul><table><tbody><tr><td colspan=\"2\">x</td></tr></tbody></table>" +
            "<div data-clause-id=\"c1\" data-clause-version=\"3\" data-locked=\"true\"><p>Clause verrouillée</p></div>";

        var clean = SafeHtml.Clean(html);

        Assert.Contains("text-align: center", clean.Replace("text-align:center", "text-align: center"));
        Assert.Contains("{{client_name}}", clean);
        Assert.Contains("<strong>", clean);
        Assert.Contains("class=\"clause\"", clean);
        Assert.Contains("colspan=\"2\"", clean);
        Assert.Contains("data-clause-version=\"3\"", clean);
        // The condition's ">" is escaped as text; the renderer decodes it before reading the condition.
        Assert.Contains("{{#if amount &gt; 1000}}", clean);
    }

    [Fact]
    public void A_sanitised_condition_still_renders()
    {
        var clean = SafeHtml.Clean("{{#if amount > 1000}}<p>Garantie</p>{{#else}}<p>Aucune</p>{{/if}}");

        var big = HtmlContractRenderer.Render(clean, new Dictionary<string, string> { ["amount"] = "5000" },
            new Dictionary<string, SignFlow.Domain.Enums.HtmlTemplateFieldType> { ["amount"] = SignFlow.Domain.Enums.HtmlTemplateFieldType.Number });
        var small = HtmlContractRenderer.Render(clean, new Dictionary<string, string> { ["amount"] = "10" },
            new Dictionary<string, SignFlow.Domain.Enums.HtmlTemplateFieldType> { ["amount"] = SignFlow.Domain.Enums.HtmlTemplateFieldType.Number });

        Assert.Contains("Garantie", big);
        Assert.Contains("Aucune", small);
    }
}
