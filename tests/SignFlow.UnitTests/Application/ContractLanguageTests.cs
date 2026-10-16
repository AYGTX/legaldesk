using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

public class ContractLanguageTests
{
    private const string FrenchContract =
        "Article 1 - Objet. Le présent contrat a pour objet la fourniture par le Prestataire des services décrits dans " +
        "l'annexe. Article 2 - Résiliation. Le contrat peut être résilié par l'une des parties en cas de manquement grave " +
        "de l'autre partie, après une mise en demeure restée sans effet pendant quinze jours.";

    private const string EnglishContract =
        "Clause 1 - Purpose. This agreement sets out the terms on which the Supplier shall provide the services to the " +
        "Customer. Clause 2 - Termination. Either party may terminate this agreement by written notice to the other party " +
        "if the other party is in material breach of any of its obligations.";

    [Fact]
    public void Detects_french_and_english()
    {
        Assert.Equal("fr", ContractLanguage.Detect(FrenchContract));
        Assert.Equal("en", ContractLanguage.Detect(EnglishContract));
    }

    [Fact]
    public void Too_short_to_tell_is_null()
    {
        Assert.Null(ContractLanguage.Detect("Contrat"));
        Assert.Null(ContractLanguage.Detect(null));
    }

    private sealed record Reg(string Title, string Language);

    [Fact]
    public void Keeps_only_regulations_in_the_contract_language()
    {
        Reg[] regs = [new("Loi 2004-63", "fr"), new("Data protection law", "en")];

        Assert.Equal(["Loi 2004-63"], ContractLanguage.Filter(regs, r => r.Language, "fr").Select(r => r.Title));
        Assert.Equal(2, ContractLanguage.Filter(regs, r => r.Language, null).Count);
        Assert.Equal(2, ContractLanguage.Filter(regs, r => r.Language, "ar").Count);
    }
}
