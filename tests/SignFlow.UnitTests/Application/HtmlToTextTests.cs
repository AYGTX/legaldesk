using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// The AI reviews read a template contract as plain text. These cases pin down what that text looks like: articles stay
/// on their own lines, markup disappears, and the answers the renderer HTML-encoded come back as the characters the
/// signer saw.
/// </summary>
public class HtmlToTextTests
{
    [Fact]
    public void Headings_and_paragraphs_become_separate_lines()
    {
        var text = HtmlToText.Convert("<h2>Article 1 — Objet</h2><p>Le Prestataire fournit les services.</p><p>Le Client paie.</p>");

        Assert.Equal("Article 1 — Objet\nLe Prestataire fournit les services.\nLe Client paie.", text);
    }

    [Fact]
    public void Tags_attributes_and_inline_styles_disappear()
    {
        var text = HtmlToText.Convert("<p style=\"text-align:center\"><strong>Contrat</strong> de <em>prestation</em></p>");

        Assert.Equal("Contrat de prestation", text);
    }

    [Fact]
    public void Entities_the_renderer_encoded_are_decoded()
    {
        // HtmlContractRenderer HTML-encodes every answer, so "Smith & Sons" arrives as "Smith &amp; Sons".
        var text = HtmlToText.Convert("<p>Client : Smith &amp; Sons — 5 &lt; 10 &quot;TND&quot;</p>");

        Assert.Equal("Client : Smith & Sons — 5 < 10 \"TND\"", text);
    }

    [Fact]
    public void List_items_and_table_rows_keep_their_structure()
    {
        var text = HtmlToText.Convert(
            "<ul><li>Livraison</li><li>Recette</li></ul><table><tr><td>Montant</td><td>45000 TND</td></tr></table>");

        Assert.Equal("- Livraison\n- Recette\nMontant | 45000 TND", text);
    }

    [Fact]
    public void Line_breaks_split_lines_and_blank_runs_collapse()
    {
        var text = HtmlToText.Convert("<p>Ligne 1<br>Ligne 2</p><p></p><p></p><p>Ligne 3</p>");

        Assert.Equal("Ligne 1\nLigne 2\n\nLigne 3", text);
    }

    [Fact]
    public void Scripts_styles_and_comments_are_not_content()
    {
        var text = HtmlToText.Convert("<style>p{color:red}</style><!-- note --><script>alert(1)</script><p>Texte</p>");

        Assert.Equal("Texte", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_in_gives_nothing_out(string? html) => Assert.Equal(string.Empty, HtmlToText.Convert(html));
}
