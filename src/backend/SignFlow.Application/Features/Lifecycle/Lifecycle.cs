using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using ValidationException = SignFlow.Application.Common.Exceptions.ValidationException;

namespace SignFlow.Application.Features.Lifecycle;

// ------------------------------------------------------------------ models

/// <summary>A contract's term as the screens show it (R-M10-1), with what is computed from it.</summary>
public record ContractTermDto(
    DateTime? StartDate,
    DateTime? InitialEndDate,
    DateTime? CurrentEndDate,
    RenewalMode RenewalMode,
    int? RenewalMonths,
    int? NoticeAmount,
    NoticeUnit NoticeUnit,
    NoticeForm? NoticeForm,
    bool Confirmed,
    string? ConfirmedByName,
    DateTime? ConfirmedAt,
    DateTime? NoticeDeadline,
    LifecycleStage Stage,
    int? DaysToEnd,
    int? DaysToNoticeDeadline,
    TermDecision Decision,
    DateTime? DecisionAt,
    DateTime? NoticeSentAt,
    bool HasNoticeProof);

public record ObligationDto(
    Guid Id,
    string Title,
    ObligationKind Kind,
    DateTime? DueDate,
    ObligationRecurrence Recurrence,
    Guid? OwnerId,
    string? OwnerName,
    string? Quote,
    ObligationSource Source,
    ObligationStatus Status,
    bool Confirmed);

public record ContractEventDto(ContractEventKind Kind, DateTime At, string? ByName, string? Details);

internal static class LifecycleReads
{
    internal static ContractTermDto? ToDto(ContractTerm? term, IReadOnlyDictionary<Guid, string> names, DateTime now)
    {
        if (term is null) return null;
        var status = TermLadder.Status(term, now);
        return new ContractTermDto(term.StartDate, term.InitialEndDate, term.CurrentEndDate, term.RenewalMode, term.RenewalMonths,
            term.NoticeAmount, term.NoticeUnit, term.NoticeForm, term.IsConfirmed,
            term.ConfirmedById is { } c ? names.GetValueOrDefault(c) : null, term.ConfirmedAt, term.NoticeDeadline,
            status.Stage, status.DaysToEnd, status.DaysToNoticeDeadline, term.Decision, term.DecisionAt, term.NoticeSentAt,
            term.NoticeProofPath is not null);
    }

    internal static ObligationDto ToDto(Obligation o, IReadOnlyDictionary<Guid, string> names) => new(
        o.Id, o.Title, o.Kind, o.DueDate, o.Recurrence, o.OwnerId, o.OwnerId is { } id ? names.GetValueOrDefault(id) : null,
        o.Quote, o.Source, o.Status, o.Confirmed);

    /// <summary>Owner or contract administrator: the dates drive alerts to the whole team.</summary>
    internal static async Task<Document> LoadForEditAsync(IApplicationDbContext db, ICurrentUserService user, Guid documentId, CancellationToken ct)
    {
        var document = await db.Documents.Include(d => d.Term).Include(d => d.Obligations)
            .FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(Document), documentId);
        if (!user.Can(Permissions.ManageAllContracts) && document.OwnerId != user.UserId)
            throw new ForbiddenAccessException();
        return document;
    }
}

// ------------------------------------------------------------------ term

/// <summary>Records the term a person checked against the contract (R-M10-1). It re-arms the alert ladder.</summary>
public record SetContractTermCommand(
    Guid DocumentId,
    DateTime? StartDate,
    DateTime? EndDate,
    RenewalMode RenewalMode,
    int? RenewalMonths,
    int? NoticeAmount,
    NoticeUnit NoticeUnit,
    NoticeForm? NoticeForm) : ICommand;

public class SetContractTermCommandValidator : AbstractValidator<SetContractTermCommand>
{
    public SetContractTermCommandValidator()
    {
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate!.Value)
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue).WithMessage("The end date must be after the start date.");
        RuleFor(x => x.RenewalMonths).NotNull().InclusiveBetween(1, 120)
            .When(x => x.RenewalMode == RenewalMode.Automatic).WithMessage("An automatic renewal needs its length in months (1 to 120).");
        RuleFor(x => x.NoticeAmount).InclusiveBetween(1, 730).When(x => x.NoticeAmount.HasValue);
    }
}

public class SetContractTermCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : ICommandHandler<SetContractTermCommand>
{
    public async Task HandleAsync(SetContractTermCommand command, CancellationToken cancellationToken = default)
    {
        var document = await LifecycleReads.LoadForEditAsync(db, currentUser, command.DocumentId, cancellationToken);
        if (command.RenewalMode == RenewalMode.Automatic && command.RenewalMonths is not (>= 1 and <= 120))
            throw new ValidationException("An automatic renewal needs its length in months (1 to 120).");
        if (command.StartDate is { } s && command.EndDate is { } e)
        {
            if (e <= s) throw new ValidationException("The end date must be after the start date.");
            var deadline = command.NoticeAmount is { } n ? (command.NoticeUnit == NoticeUnit.Months ? e.AddMonths(-n) : e.AddDays(-n)) : e;
            if (deadline <= s) throw new ValidationException("The notice period is longer than the contract term.");
        }

        var term = document.Term;
        if (term is null)
        {
            term = ContractTerm.For(document.Id);
            db.ContractTerms.Add(term);
        }
        term.Set(command.StartDate, command.EndDate, command.RenewalMode, command.RenewalMonths, command.NoticeAmount, command.NoticeUnit,
            command.NoticeForm, currentUser.UserId!.Value);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Renew, renegotiate, terminate (with the date the notice was sent and its proof) or let expire (R-M10-4).</summary>
public record DecideTermCommand(Guid DocumentId, TermDecision Decision, DateTime? NoticeSentAt, Stream? Proof, string? ProofFileName, string? ProofContentType) : ICommand;

public class DecideTermCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IStorageService storage, IAuditService audit)
    : ICommandHandler<DecideTermCommand>
{
    public async Task HandleAsync(DecideTermCommand command, CancellationToken cancellationToken = default)
    {
        var document = await LifecycleReads.LoadForEditAsync(db, currentUser, command.DocumentId, cancellationToken);
        var term = document.Term is { IsConfirmed: true } t ? t
            : throw new ConflictException("term.notConfirmed", "Record the contract's term before deciding on it.");
        if (command.Decision == TermDecision.None) throw new ValidationException("Choose a decision.");

        string? proofPath = null;
        if (command.Decision == TermDecision.Terminate)
        {
            if (command.NoticeSentAt is null)
                throw new ValidationException("Give the date the termination notice was sent.");
            if (command.Proof is null)
                throw new ValidationException("Attach the proof the notice was sent (acknowledgement of receipt, e-mail).");
            proofPath = await storage.UploadAsync(command.Proof, command.ProofFileName ?? "notice-proof",
                command.ProofContentType ?? "application/octet-stream", $"notices/{document.Id}", cancellationToken);
        }

        var userId = currentUser.UserId!.Value;
        term.Decide(command.Decision, userId, command.NoticeSentAt, proofPath);
        db.ContractEvents.Add(ContractEvent.Record(document.Id, ContractEventKind.Decision, userId, command.Decision.ToString()));
        if (command.Decision == TermDecision.Terminate)
            db.ContractEvents.Add(ContractEvent.Record(document.Id, ContractEventKind.NoticeSent, userId, $"{command.NoticeSentAt:yyyy-MM-dd}"));
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(document.Id, AuditAction.ContractTermDecided, "system",
            $"Term decision: {command.Decision}" + (command.NoticeSentAt is { } at ? $", notice sent {at:yyyy-MM-dd}" : ""),
            userId, actorEmail: currentUser.UserEmail, cancellationToken: cancellationToken);
    }
}

// ------------------------------------------------------------------ obligations

/// <summary>Adds an obligation (R-M10-5). One entered by a person is confirmed; one taken from Harvey's key data
/// carries its quote and waits for confirmation (R-M10-6).</summary>
public record AddObligationCommand(
    Guid DocumentId, string Title, ObligationKind Kind, DateTime? DueDate, ObligationRecurrence Recurrence, Guid? OwnerId,
    string? Quote, ObligationSource Source) : ICommand<Guid>;

public record UpdateObligationCommand(
    Guid DocumentId, Guid ObligationId, string Title, ObligationKind Kind, DateTime? DueDate, ObligationRecurrence Recurrence,
    Guid? OwnerId, string? Quote) : ICommand;

public enum ObligationAction { Confirm, Done, Waive, Delete }

public record ActOnObligationCommand(Guid DocumentId, Guid ObligationId, ObligationAction Action) : ICommand;

public class AddObligationCommandValidator : AbstractValidator<AddObligationCommand>
{
    public AddObligationCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Quote).MaximumLength(4000);
    }
}

public class AddObligationCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : ICommandHandler<AddObligationCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddObligationCommand command, CancellationToken cancellationToken = default)
    {
        var document = await LifecycleReads.LoadForEditAsync(db, currentUser, command.DocumentId, cancellationToken);
        if (string.IsNullOrWhiteSpace(command.Title)) throw new ValidationException("Give the obligation a title.");
        if (command.Recurrence != ObligationRecurrence.None && command.DueDate is null)
            throw new ValidationException("A recurring obligation needs its first due date.");
        // Never invented (R-M10-6): a suggestion from the text is only kept with the words it comes from.
        if (command.Source == ObligationSource.Harvey && string.IsNullOrWhiteSpace(command.Quote))
            throw new ValidationException("An obligation suggested from the contract needs the sentence it comes from.");

        var obligation = Obligation.Create(document.Id, command.Title, command.Kind, command.DueDate, command.Recurrence,
            command.OwnerId ?? document.OwnerId, command.Quote, command.Source, confirmed: command.Source == ObligationSource.Person);
        db.Obligations.Add(obligation);
        await db.SaveChangesAsync(cancellationToken);
        return obligation.Id;
    }
}

public class UpdateObligationCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : ICommandHandler<UpdateObligationCommand>
{
    public async Task HandleAsync(UpdateObligationCommand command, CancellationToken cancellationToken = default)
    {
        var document = await LifecycleReads.LoadForEditAsync(db, currentUser, command.DocumentId, cancellationToken);
        var obligation = document.Obligations.FirstOrDefault(o => o.Id == command.ObligationId)
            ?? throw new NotFoundException(nameof(Obligation), command.ObligationId);
        if (string.IsNullOrWhiteSpace(command.Title)) throw new ValidationException("Give the obligation a title.");
        obligation.Update(command.Title, command.Kind, command.DueDate, command.Recurrence, command.OwnerId, command.Quote);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public class ActOnObligationCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : ICommandHandler<ActOnObligationCommand>
{
    public async Task HandleAsync(ActOnObligationCommand command, CancellationToken cancellationToken = default)
    {
        var document = await LifecycleReads.LoadForEditAsync(db, currentUser, command.DocumentId, cancellationToken);
        var obligation = document.Obligations.FirstOrDefault(o => o.Id == command.ObligationId)
            ?? throw new NotFoundException(nameof(Obligation), command.ObligationId);
        switch (command.Action)
        {
            case ObligationAction.Confirm:
                if (obligation.DueDate is null) throw new ValidationException("Give the due date before confirming the obligation.");
                obligation.Confirm();
                break;
            case ObligationAction.Done: obligation.MarkDone(); break;
            case ObligationAction.Waive: obligation.Waive(); break;
            case ObligationAction.Delete: obligation.SoftDelete(); break;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The term, its history and the obligations of a contract, for the contract page.</summary>
public record GetContractLifecycleDetailQuery(Guid DocumentId) : IQuery<ContractLifecycleDetailDto>;

public record ContractLifecycleDetailDto(ContractTermDto? Term, List<ObligationDto> Obligations, List<ContractEventDto> Events);

public class GetContractLifecycleDetailQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IQueryHandler<GetContractLifecycleDetailQuery, ContractLifecycleDetailDto>
{
    public async Task<ContractLifecycleDetailDto> HandleAsync(GetContractLifecycleDetailQuery query, CancellationToken cancellationToken = default)
    {
        if (!await DocumentReadAccess.CanReadAsync(db, currentUser, query.DocumentId, cancellationToken))
            throw new ForbiddenAccessException();

        var term = await db.ContractTerms.AsNoTracking().FirstOrDefaultAsync(t => t.DocumentId == query.DocumentId, cancellationToken);
        var obligations = await db.Obligations.AsNoTracking().Where(o => o.DocumentId == query.DocumentId)
            .OrderBy(o => o.Status).ThenBy(o => o.DueDate).ToListAsync(cancellationToken);
        var events = await db.ContractEvents.AsNoTracking().Where(e => e.DocumentId == query.DocumentId)
            .OrderByDescending(e => e.At).Take(50).ToListAsync(cancellationToken);

        var ids = obligations.Where(o => o.OwnerId != null).Select(o => o.OwnerId!.Value)
            .Concat(events.Where(e => e.ById != null).Select(e => e.ById!.Value))
            .Concat(term?.ConfirmedById is { } c ? [c] : []).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName }).ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        return new ContractLifecycleDetailDto(
            LifecycleReads.ToDto(term, names, DateTime.UtcNow),
            [.. obligations.Select(o => LifecycleReads.ToDto(o, names))],
            [.. events.Select(e => new ContractEventDto(e.Kind, e.At, e.ById is { } b ? names.GetValueOrDefault(b) : null, e.Details))]);
    }
}
