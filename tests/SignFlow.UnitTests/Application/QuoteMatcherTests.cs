using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

public class QuoteMatcherTests
{
    private const string Contract =
        "Article 5 — Durée\nLe présent contrat est conclu pour une durée de trois (3) ans\n" +
        "et se renouvelle tacitement,   sauf dénonciation par lettre recommandée.";

    [Fact]
    public void A_word_for_word_excerpt_is_verified_even_across_line_breaks_and_spacing()
        => Assert.True(QuoteMatcher.IsVerbatim(Contract, "durée de trois (3) ans et se renouvelle tacitement, sauf"));

    [Fact]
    public void Case_accents_and_typographic_quotes_do_not_matter()
    {
        Assert.True(QuoteMatcher.IsVerbatim(Contract, "LE PRESENT CONTRAT EST CONCLU"));
        Assert.True(QuoteMatcher.IsVerbatim("L’acheteur paie le prix convenu.", "L'acheteur paie le prix"));
    }

    [Fact]
    public void A_paraphrase_is_rejected()
        => Assert.False(QuoteMatcher.IsVerbatim(Contract, "le contrat dure trois ans et se renouvelle automatiquement"));

    [Fact]
    public void A_changed_number_is_rejected()
        => Assert.False(QuoteMatcher.IsVerbatim(Contract, "durée de cinq (5) ans et se renouvelle"));

    [Fact]
    public void Too_short_an_excerpt_proves_nothing()
        => Assert.False(QuoteMatcher.IsVerbatim(Contract, "contrat"));
}
