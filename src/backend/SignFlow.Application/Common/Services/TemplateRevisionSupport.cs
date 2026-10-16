using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>One answer a template contract carries, with whether its owner may change it when revising.</summary>
public record RevisionField(string Key, string Label, string Type, bool IsRequired, string? OptionsJson, bool Editable);

/// <summary>One answer that differs between two versions of a template contract.</summary>
public record FieldChange(string FieldKey, string Label, string? OldValue, string? NewValue);

/// <summary>
/// What a template contract lets its owner change after a reviewer asks for changes: the answers to the template's
/// fields, nothing else. The PDF, the layout and the approval circuit belong to the template and stay as they are.
/// Shared by the revision form, the revise command and the version history so all three agree on which answers exist.
/// </summary>
public static class TemplateRevisionSupport
{
    public const string Html = "Html";
    public const string Pdf = "Pdf";

    public static string? KindOf(Document document) =>
        document.HtmlTemplateId.HasValue ? Html : document.PdfTemplateId.HasValue ? Pdf : null;

    /// <summary>
    /// The template's answer fields, signatures excluded. Null when the contract is not template-based or its
    /// template has since been deleted — without the template there is no list of questions to ask again.
    /// </summary>
    public static async Task<List<RevisionField>?> LoadFieldsAsync(
        IApplicationDbContext db, Document document, CancellationToken cancellationToken)
    {
        if (document.HtmlTemplateId is { } htmlId)
        {
            var template = await db.HtmlTemplates.AsNoTracking()
                .Include(t => t.Fields)
                .FirstOrDefaultAsync(t => t.Id == htmlId, cancellationToken);

            return template?.Fields
                .Where(f => f.Type != HtmlTemplateFieldType.Signature)
                .OrderBy(f => f.Order)
                .Select(f => new RevisionField(
                    f.FieldKey, f.Label, f.Type.ToString(), f.IsRequired, f.OptionsJson,
                    Editable: f.Role == HtmlTemplateFieldRole.Initiator && string.IsNullOrEmpty(f.AutoFillFrom)))
                .ToList();
        }

        if (document.PdfTemplateId is { } pdfId)
        {
            var template = await db.PdfTemplates.AsNoTracking()
                .Include(t => t.Fields)
                .FirstOrDefaultAsync(t => t.Id == pdfId, cancellationToken);

            return template?.Fields
                .Where(f => f.Type != PdfFieldType.Signature)
                .OrderBy(f => f.Order)
                .Select(f => new RevisionField(
                    f.FieldKey, f.Label, f.Type.ToString(), f.IsRequired, f.OptionsJson,
                    Editable: f.FillRole == PdfFieldFillRole.Requester && string.IsNullOrEmpty(f.AutoFillFrom)))
                .ToList();
        }

        return null;
    }

    public static Dictionary<string, string> ParseValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Answers that differ, in the template's field order; keys the template does not know come last.</summary>
    public static List<FieldChange> Diff(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after,
        IReadOnlyList<RevisionField> fields)
    {
        string Get(IReadOnlyDictionary<string, string> d, string key) => d.TryGetValue(key, out var v) ? v : string.Empty;

        var known = fields.Select(f => (f.Key, f.Label));
        var unknown = before.Keys.Concat(after.Keys)
            .Where(k => fields.All(f => f.Key != k))
            .Distinct()
            .Select(k => (Key: k, Label: k));

        return known.Concat(unknown)
            .Select(f => new FieldChange(f.Key, f.Label, Get(before, f.Key), Get(after, f.Key)))
            .Where(c => !string.Equals(c.OldValue, c.NewValue, StringComparison.Ordinal))
            .ToList();
    }
}
