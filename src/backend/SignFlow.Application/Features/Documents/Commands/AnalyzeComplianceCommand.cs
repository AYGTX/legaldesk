using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Serialization;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// Checks a contract against the published regulatory watch registry on demand — a keyword scan,
/// not a legal opinion. Scoped to the contract's family when the regulatory text is scoped, and to
/// every unscoped published text.
/// </summary>
public record AnalyzeComplianceCommand(Guid DocumentId) : ICommand<ComplianceAnalysisDto>;

public record ComplianceAnalysisDto(string ComplianceAnalysisJson, DateTime GeneratedAt);

public class AnalyzeComplianceCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    IAiService aiService,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<AnalyzeComplianceCommandHandler> logger) : ICommandHandler<AnalyzeComplianceCommand, ComplianceAnalysisDto>
{
    public async Task<ComplianceAnalysisDto> HandleAsync(AnalyzeComplianceCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        var isAdmin = currentUser.IsInRole("Administrator");
        if (!isAdmin && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        // HTML-template documents carry no PDF to read.
        if (document.HtmlTemplateId.HasValue)
            throw new Common.Exceptions.ValidationException("Only PDF contracts can be analysed.");

        var regulations = await db.RegulatoryTexts.AsNoTracking()
            .Where(r => r.Status == RegulatoryTextStatus.Published
                        && (r.RequestTypeId == null || r.RequestTypeId == document.RequestTypeId))
            .Select(r => new RegulatoryTextSummary(r.Id, r.Title, r.Keywords, r.Reference))
            .ToListAsync(cancellationToken);

        await using var pdfStream = await storage.DownloadAsync(document.StoragePath, cancellationToken);
        var text = await pdfService.ExtractTextAsync(pdfStream, cancellationToken);

        var result = await aiService.AnalyzeContractComplianceAsync(text, document.Title, regulations, cancellationToken);

        document.SetAiComplianceAnalysis(JsonSerializer.Serialize(result, ContractJson.Options));
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.ComplianceAnalysed,
            "system",
            $"Compliance analysis run — score {result.ComplianceScore}/100, {result.Findings.Count} regulation(s) checked",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("Contract {DocumentId} compliance-analysed by user {UserId}: score {Score}, {Count} regulation(s)",
            document.Id, currentUser.UserId, result.ComplianceScore, result.Findings.Count);

        return new ComplianceAnalysisDto(document.AiComplianceAnalysisJson!, document.AiComplianceAnalysisGeneratedAt!.Value);
    }
}
