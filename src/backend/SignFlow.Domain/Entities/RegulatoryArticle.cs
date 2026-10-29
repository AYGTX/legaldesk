namespace SignFlow.Domain.Entities;

/// <summary>
/// One article of a <see cref="RegulatoryText"/>, word for word. This is the unit a finding cites: "Article 12 says
/// ...", with the sentence quoted. Keeping the law at article level — instead of one summary per law — is what lets a
/// review point at the exact provision and lets a quote be checked against the stored text.
/// </summary>
public class RegulatoryArticle : TenantEntity
{
    public Guid RegulatoryTextId { get; private set; }
    public RegulatoryText RegulatoryText { get; private set; } = null!;

    /// <summary>
    /// The article's number as the text prints it: "12", "1" (for "premier"), "12 bis". A string, because laws number
    /// their articles with suffixes and amendments insert "bis" and "ter" articles between existing ones.
    /// </summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>The article's full text, as published.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>Position in the law, so articles list in reading order whatever their numbers are.</summary>
    public int Order { get; private set; }

    private RegulatoryArticle() { }

    public static RegulatoryArticle Create(Guid regulatoryTextId, string number, string text, int order)
        => new()
        {
            RegulatoryTextId = regulatoryTextId,
            Number = number.Trim(),
            Text = text.Trim(),
            Order = order
        };
}
