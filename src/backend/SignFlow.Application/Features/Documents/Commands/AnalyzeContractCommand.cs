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
/// Runs the full model-backed legal review of a contract on demand. Unlike the rules-based scan at
/// upload time this takes tens of seconds, so it is triggered explicitly by the owner rather than
/// inline with the upload.
/// </summary>
public record AnalyzeContractCommand(Guid DocumentId) : ICommand<ContractAnalysisDto>;

public record ContractAnalysisDto(
    string RiskAnalysisJson,
    DateTime GeneratedAt,
    DateTime? ObligationDueDate,
    string? ObligationType,
    string? ObligationRationale,
    bool ObligationConfirmed);

public class AnalyzeContractCommandHandler(
    IApplicationDbContext db,
    IContractTextProvider textProvider,
    IAiService aiService,
    ILegalPlaybookService playbook,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<AnalyzeContractCommandHandler> logger) : ICommandHandler<AnalyzeContractCommand, ContractAnalysisDto>
{
    public async Task<ContractAnalysisDto> HandleAsync(AnalyzeContractCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        var isAdmin = currentUser.IsInRole("Administrator");
        if (!isAdmin && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        var text = await textProvider.GetTextAsync(document, cancellationToken);

        var result = await aiService.AnalyzeContractAsync(text, document.Title, DateTime.UtcNow, cancellationToken);
        result = result with { Risk = await playbook.AttachToRiskAsync(result.Risk, text, cancellationToken) };

        document.SetAiRiskAnalysis(JsonSerializer.Serialize(result.Risk, ContractJson.Options));

        // A date the owner has already confirmed outranks a fresh suggestion — same rule ApplyRevision follows.
        if (!document.ObligationConfirmed && result.Obligation.DueDate.HasValue)
        {
            document.SetObligationSuggestion(
                result.Obligation.DueDate,
                result.Obligation.ObligationType,
                result.Obligation.Rationale);
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.ContractAnalysed,
            "system",
            $"Contract analysis run — risk score {result.Risk.RiskScore}/100, {result.Risk.Findings.Count} finding(s)",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("Contract {DocumentId} analysed by user {UserId}: score {Score}, {Count} findings",
            document.Id, currentUser.UserId, result.Risk.RiskScore, result.Risk.Findings.Count);

        return new ContractAnalysisDto(
            document.AiRiskAnalysisJson!,
            document.AiRiskAnalysisGeneratedAt!.Value,
            document.ObligationDueDate,
            document.ObligationType,
            document.ObligationRationale,
            document.ObligationConfirmed);
    }
}
