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

        // A scanned contract was read once by text recognition at intake; every review reads those same words.
        if (document.OcrText is not null)
            return document.OcrText;

        await using var pdf = await storage.DownloadAsync(document.StoragePath, cancellationToken);
        var pdfText = await pdfService.ExtractTextAsync(pdf, cancellationToken);

        return document.PdfTemplateId is { } pdfTemplateId
            ? pdfText + await FilledFieldsAsync(document, pdfTemplateId, cancellationToken)
            : pdfText;
    }

    /// <summary>
    /// A contract made from a PDF template keeps its blanks outside the file: the stored PDF is the template's, and the
    /// answers are drawn on it later. A review of the file alone would never see a wrong name, amount or date, so the
    /// answers are appended as "label: value" lines.
    /// </summary>
    private async Task<string> FilledFieldsAsync(Document document, Guid pdfTemplateId, CancellationToken cancellationToken)
    {
        var values = TemplateRevisionSupport.ParseValues(document.FilledValuesJson);
        if (values.Count == 0) return string.Empty;

        var labels = await db.PdfTemplateFields.AsNoTracking()
            .Where(f => f.PdfTemplateId == pdfTemplateId)
            .ToDictionaryAsync(f => f.FieldKey, f => f.Label, cancellationToken);

        var lines = values
            .Where(v => !string.IsNullOrWhiteSpace(v.Value))
            .Select(v => $"{(labels.TryGetValue(v.Key, out var label) && !string.IsNullOrWhiteSpace(label) ? label : v.Key)} : {v.Value}")
            .ToList();

        return lines.Count == 0 ? string.Empty : "\n\nInformations renseignées / Filled-in fields\n" + string.Join("\n", lines);
    }
}
