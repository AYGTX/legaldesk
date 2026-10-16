using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Services;

/// <summary>The words of a contract, wherever they live: read from the stored PDF, or rendered from a template.</summary>
public interface IContractTextProvider
{
    Task<string> GetTextAsync(Document document, CancellationToken cancellationToken = default);
}

/// <summary>
/// One answer to "what does this contract say?" for every review that needs to know — the risk review, the compliance
/// review, the pre-flight and the summary.
///
/// A PDF contract is read out of its stored file. A contract built from an HTML template has no file until it is signed;
/// its text is the template as it was when the request was made, with the answers filled in and the conditional clauses
/// resolved — rendered by the same <see cref="HtmlContractRenderer"/> the signing screen and the sealed PDF use, so the
/// review reads exactly what the signer is shown.
/// </summary>
public class ContractTextProvider(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService) : IContractTextProvider
{
    public async Task<string> GetTextAsync(Document document, CancellationToken cancellationToken = default)
    {
        if (document.HtmlTemplateId is { } templateId)
        {
            var values = TemplateRevisionSupport.ParseValues(document.FilledValuesJson);

            // Field types decide how a conditional clause compares its value; absent when the template was deleted,
            // in which case every field is text and the contract still renders.
            var fieldTypes = await db.HtmlTemplateFields.AsNoTracking()
                .Where(f => f.HtmlTemplateId == templateId)
                .ToDictionaryAsync(f => f.FieldKey, f => f.Type, cancellationToken);

            var rendered = HtmlContractRenderer.Render(document.HtmlContentSnapshot, values, fieldTypes);
            return HtmlToText.Convert(rendered);
        }

        await using var pdf = await storage.DownloadAsync(document.StoragePath, cancellationToken);
        return await pdfService.ExtractTextAsync(pdf, cancellationToken);
    }
}
