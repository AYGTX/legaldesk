using SignFlow.Application.Common.Security;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Common.Serialization;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// Reviews a contract against the published regulatory watch registry on demand — a full model
/// review, falling back to the keyword scan when the model is unavailable, and in neither case a
/// legal opinion. Scoped to the contract's family when the regulatory text is scoped, and to every
/// unscoped published text.
/// </summary>
public record AnalyzeComplianceCommand(Guid DocumentId) : ICommand<ComplianceAnalysisDto>;

public record ComplianceAnalysisDto(string ComplianceAnalysisJson, DateTime GeneratedAt);

public class AnalyzeComplianceCommandHandler(
    IApplicationDbContext db,
    IContractTextProvider textProvider,
    IGroundedComplianceReview complianceReview,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<AnalyzeComplianceCommandHandler> logger) : ICommandHandler<AnalyzeComplianceCommand, ComplianceAnalysisDto>
{
    public async Task<ComplianceAnalysisDto> HandleAsync(AnalyzeComplianceCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        var isAdmin = currentUser.Can(Permissions.ManageAllContracts);
        if (!isAdmin && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        var published = await db.RegulatoryTexts.AsNoTracking()
            .Where(r => r.Status == RegulatoryTextStatus.Published
                        && (r.RequestTypeId == null || r.RequestTypeId == document.RequestTypeId))
            .Select(r => new { Summary = new RegulatoryTextSummary(r.Id, r.Title, r.Keywords, r.Reference), r.Language })
            .ToListAsync(cancellationToken);

        var text = await textProvider.GetTextAsync(document, cancellationToken);

        var regulations = ContractLanguage.Filter(published, r => r.Language, ContractLanguage.Detect(text))
            .Select(r => r.Summary).ToList();

        var result = await complianceReview.ReviewAsync(text, document.Title, regulations, cancellationToken);

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
