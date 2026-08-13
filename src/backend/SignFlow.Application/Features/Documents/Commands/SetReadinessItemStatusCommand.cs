using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// Ticks a pre-flight to-do off (or dismisses it as not applicable) and re-grades the contract from
/// what is left open — no re-analysis, so the verdict responds immediately.
/// </summary>
public record SetReadinessItemStatusCommand(
    Guid DocumentId,
    Guid ItemId,
    ReadinessItemStatus Status,
    string? Note) : ICommand<ReadinessDto>;

public class SetReadinessItemStatusCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IAuditService auditService) : ICommandHandler<SetReadinessItemStatusCommand, ReadinessDto>
{
    public async Task<ReadinessDto> HandleAsync(SetReadinessItemStatusCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.ReadinessItems)
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        var isAdmin = currentUser.IsInRole("Administrator");
        if (!isAdmin && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        var item = document.ReadinessItems.FirstOrDefault(i => i.Id == command.ItemId)
            ?? throw new NotFoundException(nameof(DocumentReadinessItem), command.ItemId);

        item.SetStatus(command.Status, currentUser.UserId, command.Note);

        var open = document.ReadinessItems.Where(i => i.Status == ReadinessItemStatus.Open).ToList();

        // Re-grading without re-analysing: the stored analysis scores still describe this file.
        var riskScore = ReadinessDto.ScoreFrom(document.AiRiskAnalysisJson, "riskScore");
        var complianceScore = ReadinessDto.ScoreFrom(document.AiComplianceAnalysisJson, "complianceScore");

        var assessment = ReadinessEvaluator.Assess(
            document.ReadinessItems.Count,
            [.. open.Select(i => i.Severity)],
            riskScore,
            complianceScore);

        document.SetReadiness(assessment.Verdict, assessment.Score);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.ReadinessItemUpdated,
            "system",
            $"Pre-flight item '{item.Title}' marked {command.Status} — verdict now {assessment.Verdict}",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        return ReadinessDto.From(document, assessment, riskScore, complianceScore);
    }
}
