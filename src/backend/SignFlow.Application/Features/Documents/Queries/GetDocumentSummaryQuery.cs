using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.Application.Features.Documents.Queries;

public record GetDocumentSummaryQuery(Guid DocumentId) : IQuery<AiDocumentSummaryDto>;

public record AiDocumentSummaryDto(string Summary, DateTime GeneratedAt);

public class GetDocumentSummaryQueryHandler(
    IApplicationDbContext db,
    IContractTextProvider textProvider,
    IAiService aiService) : IQueryHandler<GetDocumentSummaryQuery, AiDocumentSummaryDto>
{
    public async Task<AiDocumentSummaryDto> HandleAsync(GetDocumentSummaryQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Document", query.DocumentId);

        // Return persisted summary if already generated
        if (document.AiSummary is not null)
            return new AiDocumentSummaryDto(document.AiSummary, document.AiSummaryGeneratedAt!.Value);

        // Generate, persist, and return
        var text = await textProvider.GetTextAsync(document, cancellationToken);
        var summary = await aiService.SummarizeDocumentAsync(text, document.Title, cancellationToken);

        document.SetAiSummary(summary);
        await db.SaveChangesAsync(cancellationToken);

        return new AiDocumentSummaryDto(summary, document.AiSummaryGeneratedAt!.Value);
    }
}
