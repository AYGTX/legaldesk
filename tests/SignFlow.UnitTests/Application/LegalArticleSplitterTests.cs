using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// The splitter cuts the text of a law into articles so a person can check and sign off each one. The cases are the
/// headings these laws actually use — French, Arabic, "bis" insertions, "premier" — and the mistakes it must flag
/// rather than hide. The sample sentences are invented for the test; they are not quotations of any law.
/// </summary>
public class LegalArticleSplitterTests
{
    [Fact]
    public void French_headings_with_a_dash_split_into_numbered_articles()
    {
        var split = LegalArticleSplitter.Split(
            "Article 1 - Le présent texte fixe les règles.\nIl s'applique à tous.\nArticle 2 - Les définitions suivantes s'appliquent.\nArticle 3 - Les parties conviennent.");

        Assert.Equal(["1", "2", "3"], split.Articles.Select(a => a.Number));
        Assert.Equal("Le présent texte fixe les règles.\nIl s'applique à tous.", split.Articles[0].Text);
        Assert.Empty(split.Warnings);
    }

    [Theory]
    [InlineData("Article premier : Texte.", "1")]
    [InlineData("Article 1er - Texte.", "1")]
    [InlineData("ARTICLE 7. Texte.", "7")]
    [InlineData("Art. 12 – Texte.", "12")]
    [InlineData("Article 12 bis - Texte.", "12 bis")]
    [InlineData("Article 5 (nouveau) - Texte.", "5")]
    public void Headings_in_the_forms_laws_use_are_recognised(string line, string expectedNumber)
    {
        var split = LegalArticleSplitter.Split(line);

        Assert.Equal(expectedNumber, Assert.Single(split.Articles).Number);
    }

    [Fact]
    public void An_amendment_marker_stays_with_the_text_because_it_is_part_of_what_was_published()
    {
        var split = LegalArticleSplitter.Split("Article 5 (nouveau) - Le délai est de trente jours.");

        Assert.Equal("5", split.Articles[0].Number);
        Assert.StartsWith("(nouveau)", split.Articles[0].Text);
        Assert.Contains("trente jours", split.Articles[0].Text);
    }

    [Fact]
    public void Arabic_headings_and_arabic_indic_digits_are_normalised()
    {
        var split = LegalArticleSplitter.Split("الفصل الأول - نص.\nالفصل ٢ - نص ثان.\nالفصل 3 - نص ثالث.");

        Assert.Equal(["1", "2", "3"], split.Articles.Select(a => a.Number));
        Assert.Empty(split.Warnings);
    }

    [Fact]
    public void A_sentence_that_merely_starts_with_the_word_article_is_not_a_heading()
    {
        var split = LegalArticleSplitter.Split(
            "Article 1 - Dispositions générales.\nArticle 2 de la présente loi est applicable aux contrats en cours.\nArticle 2 - Définitions.");

        Assert.Equal(["1", "2"], split.Articles.Select(a => a.Number));
        Assert.Contains("Article 2 de la présente loi est applicable", split.Articles[0].Text);
    }

    [Fact]
    public void Text_before_the_first_article_is_kept_as_the_preamble_not_lost_and_not_an_article()
    {
        var split = LegalArticleSplitter.Split("Loi n° 00-000 du 1er janvier 2000\nVu la constitution\nArticle 1 - Texte.");

        Assert.Equal("Loi n° 00-000 du 1er janvier 2000\nVu la constitution", split.Preamble);
        Assert.Single(split.Articles);
    }

    [Fact]
    public void A_gap_in_the_numbering_is_flagged_not_filled()
    {
        var split = LegalArticleSplitter.Split("Article 1 - A.\nArticle 2 - B.\nArticle 5 - C.");

        Assert.Equal(["1", "2", "5"], split.Articles.Select(a => a.Number));
        Assert.Contains(split.Warnings, w => w.Contains("3–4") && w.Contains("missing"));
    }

    [Fact]
    public void A_repeated_number_is_flagged()
    {
        var split = LegalArticleSplitter.Split("Article 1 - A.\nArticle 2 - B.\nArticle 2 - C.");

        Assert.Contains(split.Warnings, w => w.Contains("Article 2 appears more than once"));
    }

    [Fact]
    public void A_step_backwards_is_flagged_as_wrong_order()
    {
        var split = LegalArticleSplitter.Split("Article 1 - A.\nArticle 2 - B.\nArticle 5 - C.\nArticle 4 - D.");

        Assert.Contains(split.Warnings, w => w.Contains("Article 4 comes after article 5"));
    }

    [Fact]
    public void Inserted_bis_articles_do_not_break_the_sequence_check()
    {
        var split = LegalArticleSplitter.Split("Article 1 - A.\nArticle 2 - B.\nArticle 2 bis - C.\nArticle 3 - D.");

        Assert.Equal(["1", "2", "2 bis", "3"], split.Articles.Select(a => a.Number));
        Assert.Empty(split.Warnings);
    }

    [Fact]
    public void An_article_with_no_text_is_flagged()
    {
        var split = LegalArticleSplitter.Split("Article 1 - A.\nArticle 2\nArticle 3 - C.");

        Assert.Contains(split.Warnings, w => w.Contains("Article 2 has no text"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Nothing_to_split_says_so(string? raw)
    {
        var split = LegalArticleSplitter.Split(raw);

        Assert.Empty(split.Articles);
        Assert.Single(split.Warnings);
    }

    [Fact]
    public void Text_with_no_headings_returns_no_articles_and_explains_why()
    {
        var split = LegalArticleSplitter.Split("Un paragraphe sans aucun intitulé d'article.");

        Assert.Empty(split.Articles);
        Assert.Contains(split.Warnings, w => w.Contains("No article headings"));
    }

    [Fact]
    public void Windows_line_endings_are_handled()
    {
        var split = LegalArticleSplitter.Split("Article 1 - A.\r\nsuite.\r\nArticle 2 - B.");

        Assert.Equal(["1", "2"], split.Articles.Select(a => a.Number));
        Assert.Equal("A.\nsuite.", split.Articles[0].Text);
    }
}
