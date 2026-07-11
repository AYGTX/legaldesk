using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.Documents.Queries;

public record GetDocumentSummaryQuery(Guid DocumentId) : IQuery<AiDocumentSummaryDto>;

public record AiDocumentSummaryDto(string Summary, DateTime GeneratedAt);

public class GetDocumentSummaryQueryHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    IAiService aiService) : IQueryHandler<GetDocumentSummaryQuery, AiDocumentSummaryDto>
{
    public async Task<AiDocumentSummaryDto> HandleAsync(GetDocumentSummaryQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Document", query.DocumentId);

        // HTML template documents have no PDF file — return a canned summary
        if (document.HtmlTemplateId.HasValue)
            return new AiDocumentSummaryDto("This is an HTML form document — no PDF summary available.", DateTime.UtcNow);

        // Return persisted summary if already generated
        if (document.AiSummary is not null)
            return new AiDocumentSummaryDto(document.AiSummary, document.AiSummaryGeneratedAt!.Value);

        // Generate, persist, and return
        await using var pdfStream = await storage.DownloadAsync(document.StoragePath, cancellationToken);
        var text = await pdfService.ExtractTextAsync(pdfStream, cancellationToken);
        var summary = await aiService.SummarizeDocumentAsync(text, document.Title, cancellationToken);

        document.SetAiSummary(summary);
        await db.SaveChangesAsync(cancellationToken);

        return new AiDocumentSummaryDto(summary, document.AiSummaryGeneratedAt!.Value);
    }
}
