using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Queries;

/// <summary>Every round the contract went through (R-M7-2), newest first: what version it asked people to act on, who
/// did what, and how it ended. A revision never erases a round.</summary>
public record GetDocumentRoundsQuery(Guid DocumentId) : IQuery<IReadOnlyList<ContractRoundDto>>;

public record ContractRoundDto(
    Guid Id,
    int Number,
    int VersionNumber,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    RoundOutcome Outcome,
    IReadOnlyList<RoundStepDto> Steps);

/// <param name="ActedByName">For a team or role step, the member who acted.</param>
/// <param name="HandedOver">The participant handed this step to someone else, whose step follows.</param>
public record RoundStepDto(
    int Order,
    string Name,
    ApprovalType ApprovalType,
    StageRule StageRule,
    WorkflowStepStatus Status,
    DateTime? OpenedAt,
    DateTime? DueAt,
    DateTime? CompletedAt,
    string? DeclineReason,
    string? ActedByName,
    bool HandedOver,
    bool StandsIn);

public class GetDocumentRoundsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetDocumentRoundsQuery, IReadOnlyList<ContractRoundDto>>
{
    public async Task<IReadOnlyList<ContractRoundDto>> HandleAsync(GetDocumentRoundsQuery query, CancellationToken cancellationToken = default)
    {
        await DocumentVersionAccess.LoadAsync(db, currentUser, query.DocumentId, cancellationToken);

        var rounds = await db.ContractRounds.AsNoTracking()
            .Where(r => r.DocumentId == query.DocumentId)
            .OrderByDescending(r => r.Number)
            .ToListAsync(cancellationToken);
        if (rounds.Count == 0) return [];

        var roundIds = rounds.Select(r => (Guid?)r.Id).ToList();
        var steps = await db.WorkflowSteps.AsNoTracking()
            .Where(s => roundIds.Contains(s.RoundId))
            .OrderBy(s => s.Order).ThenBy(s => s.CreatedAt)
            .Select(s => new
            {
                s.RoundId, s.Id, s.Order, s.Name, s.ApprovalType, s.StageRule, s.Status, s.OpenedAt, s.DueAt, s.CompletedAt,
                s.DeclineReason, s.ActedByUserId, s.DelegatedFromStepId
            })
            .ToListAsync(cancellationToken);

        var actorIds = steps.Where(s => s.ActedByUserId is not null).Select(s => s.ActedByUserId!.Value).Distinct().ToList();
        var actors = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        var handedOver = steps.Where(s => s.DelegatedFromStepId is not null).Select(s => s.DelegatedFromStepId!.Value).ToHashSet();

        return rounds.Select(r => new ContractRoundDto(
            r.Id, r.Number, r.VersionNumber, r.OpenedAt, r.ClosedAt, r.Outcome,
            steps.Where(s => s.RoundId == r.Id).Select(s => new RoundStepDto(
                s.Order, s.Name, s.ApprovalType, s.StageRule, s.Status, s.OpenedAt, s.DueAt, s.CompletedAt, s.DeclineReason,
                s.ActedByUserId is { } actor && actors.TryGetValue(actor, out var name) ? name : null,
                handedOver.Contains(s.Id),
                s.DelegatedFromStepId is not null)).ToList())).ToList();
    }
}
