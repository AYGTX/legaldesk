using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.Application.Features.Documents.Queries;

/// <summary>
/// The current version as an editable Word file (R-M7-3): the owner edits it in Word and uploads it back as the next
/// version. Only uploaded contracts have one; a template contract is revised by changing its answers.
/// </summary>
public record GetDocumentDocxQuery(Guid DocumentId) : IQuery<DocumentDocxDto>;

public record DocumentDocxDto(byte[] Content, string FileName);

public class GetDocumentDocxQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IContractTextProvider texts) : IQueryHandler<GetDocumentDocxQuery, DocumentDocxDto>
{
    public async Task<DocumentDocxDto> HandleAsync(GetDocumentDocxQuery query, CancellationToken cancellationToken = default)
    {
        var document = await DocumentVersionAccess.LoadAsync(db, currentUser, query.DocumentId, cancellationToken);

        if (document.HtmlTemplateId is not null || document.PdfTemplateId is not null)
            throw new ConflictException("revision.docx_template", "This contract was built from a template: revise its answers instead.");

        var text = await texts.GetTextAsync(document, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            throw new ConflictException("revision.docx_no_text", "This contract has no readable text to put in a Word file.");

        var stem = Path.GetFileNameWithoutExtension(document.FileName);
        return new DocumentDocxDto(
            DocxRoundTrip.Write(document.Title, text),
            $"{stem}-v{document.Version}.docx");
    }
}
