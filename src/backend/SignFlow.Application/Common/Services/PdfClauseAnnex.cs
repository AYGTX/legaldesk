using System.Net;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Builds the annex that carries library clauses onto a PDF contract. A PDF template is a fixed file, so its own text
/// cannot take a clause; the approved wording goes on extra pages appended after it, drawn with the same theme as
/// every other contract. The pages are part of the document from the moment it is created, so they are covered by its
/// hash and seal like the rest.
/// </summary>
public static class PdfClauseAnnex
{
    public record AnnexClause(string Title, string Body, string Language);

    /// <summary>
    /// The annex as a themed HTML fragment. Each clause body is rendered like contract text: its conditional blocks are
    /// resolved against the answers given on the request (<paramref name="fieldTypes"/> says how to compare each one),
    /// and any placeholder left over leaves no trace.
    /// </summary>
    public static string BuildHtml(
        IReadOnlyList<AnnexClause> clauses,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, SignFlow.Domain.Enums.HtmlTemplateFieldType>? fieldTypes = null)
    {
        var french = clauses.Count == 0 || !clauses.All(c => string.Equals(c.Language, "en", StringComparison.OrdinalIgnoreCase));

        var html = new System.Text.StringBuilder();
        html.Append("<header class=\"cd-header\">")
            .Append("<p class=\"cd-eyebrow\">").Append(french ? "ANNEXE" : "ANNEX").Append("</p>")
            .Append("<h1>").Append(french ? "Clauses complémentaires" : "Additional clauses").Append("</h1>")
            .Append("<p class=\"cd-subtitle\">")
            .Append(french
                ? "Clauses approuvées de la bibliothèque, annexées au contrat."
                : "Approved clauses from the library, annexed to the contract.")
            .Append("</p></header>");

        foreach (var clause in clauses)
        {
            html.Append("<section class=\"cd-section\"><h2>").Append(WebUtility.HtmlEncode(clause.Title)).Append("</h2>")
                .Append(HtmlContractRenderer.Render(clause.Body, values, fieldTypes))
                .Append("</section>");
        }

        return html.ToString();
    }
}
