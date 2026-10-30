using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;

namespace SignFlow.IntegrationTests.Stubs;

/// <summary>Every contract reads as <see cref="Text"/>: scenarios test the review, not PDF extraction.</summary>
public sealed class FixedTextProvider : IContractTextProvider
{
    public string Text { get; set; } =
        "ARTICLE 1 - OBJET Le présent contrat a pour objet la fourniture de services informatiques. " +
        "ARTICLE 2 - RESPONSABILITÉ Le prestataire n'est responsable d'aucun dommage, quelle qu'en soit la cause. " +
        "ARTICLE 3 - DURÉE Le contrat est conclu pour une durée d'un an.";

    public Task<string> GetTextAsync(Document document, CancellationToken cancellationToken = default) => Task.FromResult(Text);
}
