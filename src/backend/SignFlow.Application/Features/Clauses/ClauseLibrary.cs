using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Clauses;

// ------------------------------------------------------------------ models

/// <summary>One wording of a clause, as the curators see it.</summary>
public record ClauseVersionDto(
    int Number,
    string Body,
    string Language,
    ClauseStatus Status,
    string? AuthorName,
    Guid AuthorId,
    string? ApprovedByName,
    DateTime? ApprovedAt,
    DateTime CreatedAt);

/// <summary>A clause with every wording it has had (M9), newest first.</summary>
public record ClauseDto(
    Guid Id,
    string Title,
    ClauseCategory Category,
    string? Guidance,
    ClauseRiskLevel? RiskLevel,
    int UsageCount,
    Guid? RequestTypeId,
    string? RequestTypeName,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<string> RuleKeys,
    List<ClauseVersionDto> Versions)
{
    public ClauseVersionDto? Approved => Versions.FirstOrDefault(v => v.Status == ClauseStatus.Approved);
    public ClauseVersionDto? Latest => Versions.FirstOrDefault();
}

/// <summary>The approved wording of a clause, as a drafter or a reviewer is offered it.</summary>
public record ApprovedClauseDto(
    Guid Id,
    string Title,
    ClauseCategory Category,
    string? Guidance,
    ClauseRiskLevel? RiskLevel,
    int Version,
    string Body,
    string Language,
    int UsageCount,
    Guid? RequestTypeId,
    string? RequestTypeName,
    List<string> RuleKeys);

internal static class ClauseReads
{
    internal static async Task<List<ClauseDto>> ToDtosAsync(IApplicationDbContext db, List<Clause> clauses, CancellationToken ct)
    {
        var userIds = clauses.SelectMany(c => c.Versions.Select(v => v.AuthorId)
                .Concat(c.Versions.Where(v => v.ApprovedById != null).Select(v => v.ApprovedById!.Value)).Append(c.CreatedById))
            .Distinct().ToList();
        var names = (await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

        return [.. clauses.Select(c => new ClauseDto(
            c.Id, c.Title, c.Category, c.Guidance, c.RiskLevel, c.UsageCount, c.RequestTypeId, c.RequestType?.Name,
            names.GetValueOrDefault(c.CreatedById), c.CreatedAt, c.UpdatedAt,
            [.. c.RuleLinks.Select(l => l.RuleKey).Order()],
            [.. c.Versions.OrderByDescending(v => v.Number).Select(v => new ClauseVersionDto(
                v.Number, v.Body, v.Language, v.Status, names.GetValueOrDefault(v.AuthorId), v.AuthorId,
                v.ApprovedById is { } a ? names.GetValueOrDefault(a) : null, v.ApprovedAt, v.CreatedAt))]))];
    }

    internal static ApprovedClauseDto? Offered(Clause c)
    {
        var v = c.Approved;
        return v is null ? null : new ApprovedClauseDto(c.Id, c.Title, c.Category, c.Guidance, c.RiskLevel, v.Number, v.Body,
            v.Language, c.UsageCount, c.RequestTypeId, c.RequestType?.Name, [.. c.RuleLinks.Select(l => l.RuleKey).Order()]);
    }

    internal static IQueryable<Clause> WithVersions(IApplicationDbContext db) =>
        db.Clauses.Include(c => c.Versions).Include(c => c.RuleLinks).Include(c => c.RequestType);

    internal static async Task<Clause> LoadAsync(IApplicationDbContext db, Guid id, CancellationToken ct) =>
        await WithVersions(db).FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException(nameof(Clause), id);
}

// ------------------------------------------------------------------ queries

/// <summary>The library for the people who curate it: every clause, every wording.</summary>
public record GetClausesQuery(
    string? Search = null,
    ClauseCategory? Category = null,
    string? Language = null,
    ClauseStatus? Status = null) : IQuery<List<ClauseDto>>;

public class GetClausesQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IQueryHandler<GetClausesQuery, List<ClauseDto>>
{
    public async Task<List<ClauseDto>> HandleAsync(GetClausesQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.EditClauses))
            throw new ForbiddenAccessException();

        var clauses = ClauseReads.WithVersions(db).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            clauses = clauses.Where(c => EF.Functions.Like(c.Title, $"%{term}%") || c.Versions.Any(v => EF.Functions.Like(v.Body, $"%{term}%")));
        }
        if (query.Category.HasValue) clauses = clauses.Where(c => c.Category == query.Category.Value);
        if (!string.IsNullOrWhiteSpace(query.Language)) clauses = clauses.Where(c => c.Versions.Any(v => v.Language == query.Language));
        if (query.Status.HasValue) clauses = clauses.Where(c => c.Versions.Any(v => v.Status == query.Status.Value));

        var list = await clauses.OrderBy(c => c.Category).ThenBy(c => c.Title).ToListAsync(cancellationToken);
        return await ClauseReads.ToDtosAsync(db, list, cancellationToken);
    }
}

/// <summary>Approved wordings offered to a drafter (any signed-in user). Drafts and deprecated wordings never are.</summary>
public record GetApprovedClausesQuery(
    string? Search = null,
    ClauseCategory? Category = null,
    string? Language = null,
    Guid? RequestTypeId = null) : IQuery<List<ApprovedClauseDto>>;

public class GetApprovedClausesQueryHandler(IApplicationDbContext db) : IQueryHandler<GetApprovedClausesQuery, List<ApprovedClauseDto>>
{
    public async Task<List<ApprovedClauseDto>> HandleAsync(GetApprovedClausesQuery query, CancellationToken cancellationToken = default)
    {
        var clauses = ClauseReads.WithVersions(db).AsNoTracking()
            .Where(c => c.Versions.Any(v => v.Status == ClauseStatus.Approved));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            clauses = clauses.Where(c => EF.Functions.Like(c.Title, $"%{term}%")
                || c.Versions.Any(v => v.Status == ClauseStatus.Approved && EF.Functions.Like(v.Body, $"%{term}%")));
        }
        if (query.Category.HasValue) clauses = clauses.Where(c => c.Category == query.Category.Value);
        if (!string.IsNullOrWhiteSpace(query.Language))
            clauses = clauses.Where(c => c.Versions.Any(v => v.Status == ClauseStatus.Approved && v.Language == query.Language));
        // A clause scoped to a family is offered to that family; an unscoped clause to every family.
        if (query.RequestTypeId.HasValue)
            clauses = clauses.Where(c => c.RequestTypeId == null || c.RequestTypeId == query.RequestTypeId.Value);

        var list = await clauses.OrderByDescending(c => c.UsageCount).ThenBy(c => c.Title).ToListAsync(cancellationToken);
        return [.. list.Select(ClauseReads.Offered).OfType<ApprovedClauseDto>()];
    }
}

/// <summary>The organisation's approved wordings for a Harvey rule (R-M9-3), in the contract's language first. A finding
/// proposes these before Harvey's own model clause.</summary>
public record GetClausesForRuleQuery(string RuleKey, string? Language = null) : IQuery<List<ApprovedClauseDto>>;

public class GetClausesForRuleQueryHandler(IApplicationDbContext db) : IQueryHandler<GetClausesForRuleQuery, List<ApprovedClauseDto>>
{
    public async Task<List<ApprovedClauseDto>> HandleAsync(GetClausesForRuleQuery query, CancellationToken cancellationToken = default)
    {
        var list = await ClauseReads.WithVersions(db).AsNoTracking()
            .Where(c => c.RuleLinks.Any(l => l.RuleKey == query.RuleKey) && c.Versions.Any(v => v.Status == ClauseStatus.Approved))
            .ToListAsync(cancellationToken);
        return [.. list.Select(ClauseReads.Offered).OfType<ApprovedClauseDto>()
            .OrderBy(c => query.Language is null || c.Language == query.Language ? 0 : 1).ThenByDescending(c => c.UsageCount)];
    }
}

// ------------------------------------------------------------------ commands

/// <summary>A new clause with its first wording, as a draft. <paramref name="RuleKey"/>: added from a finding (R-M9-4).</summary>
public record CreateClauseCommand(
    string Title,
    ClauseCategory Category,
    string Body,
    string Language,
    string? Guidance,
    ClauseRiskLevel? RiskLevel,
    Guid? RequestTypeId,
    string? RuleKey = null) : ICommand<Guid>;

public class CreateClauseCommandValidator : AbstractValidator<CreateClauseCommand>
{
    public CreateClauseCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(100_000);
        RuleFor(x => x.Guidance).MaximumLength(2000);
        RuleFor(x => x.Language).Must(l => l is "en" or "fr").WithMessage("Language must be 'en' or 'fr'.");
        RuleFor(x => x.RuleKey).MaximumLength(120);
    }
}

public class CreateClauseCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ILogger<CreateClauseCommandHandler> logger)
    : ICommandHandler<CreateClauseCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateClauseCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.EditClauses))
            throw new ForbiddenAccessException();
        var userId = currentUser.UserId!.Value;

        var clause = Clause.Create(command.Title.Trim(), command.Category, SafeHtml.Clean(command.Body), userId, command.Language,
            command.Guidance, command.RiskLevel, command.RequestTypeId);
        if (!string.IsNullOrWhiteSpace(command.RuleKey)) clause.LinkRules([command.RuleKey]);
        clause.SetCreatedBy(userId.ToString());
        db.Clauses.Add(clause);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clause '{Title}' created by {UserId}", command.Title, userId);
        return clause.Id;
    }
}

/// <summary>What describes a clause; its wording changes through <see cref="RedraftClauseCommand"/>.</summary>
public record DescribeClauseCommand(
    Guid ClauseId, string Title, ClauseCategory Category, string? Guidance, ClauseRiskLevel? RiskLevel, Guid? RequestTypeId) : ICommand;

public class DescribeClauseCommandValidator : AbstractValidator<DescribeClauseCommand>
{
    public DescribeClauseCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Guidance).MaximumLength(2000);
    }
}

public class DescribeClauseCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : ICommandHandler<DescribeClauseCommand>
{
    public async Task HandleAsync(DescribeClauseCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.EditClauses))
            throw new ForbiddenAccessException();
        var clause = await ClauseReads.LoadAsync(db, command.ClauseId, cancellationToken);
        clause.Describe(command.Title.Trim(), command.Category, command.Guidance, command.RiskLevel, command.RequestTypeId);
        clause.SetUpdatedBy(currentUser.UserId!.Value.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>New wording (R-M9-1): rewrites the open draft, or drafts the next version; an approved wording never changes.</summary>
public record RedraftClauseCommand(Guid ClauseId, string Body, string Language) : ICommand<int>;

public class RedraftClauseCommandValidator : AbstractValidator<RedraftClauseCommand>
{
    public RedraftClauseCommandValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(100_000);
        RuleFor(x => x.Language).Must(l => l is "en" or "fr").WithMessage("Language must be 'en' or 'fr'.");
    }
}

public class RedraftClauseCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : ICommandHandler<RedraftClauseCommand, int>
{
    public async Task<int> HandleAsync(RedraftClauseCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.EditClauses))
            throw new ForbiddenAccessException();
        var clause = await ClauseReads.LoadAsync(db, command.ClauseId, cancellationToken);
        var before = clause.Versions.Count;
        var version = clause.Redraft(SafeHtml.Clean(command.Body), command.Language, currentUser.UserId!.Value);
        // A new version is a new row; its key is set already, so EF is told rather than left to guess.
        if (clause.Versions.Count > before) db.ClauseVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);
        return version.Number;
    }
}

/// <summary>Approves a draft (R-M9-2): a Manager or an Administrator, never the wording's author.</summary>
public record ApproveClauseVersionCommand(Guid ClauseId, int Number) : ICommand;

public class ApproveClauseVersionCommandHandler(
    IApplicationDbContext db, ICurrentUserService currentUser, ILogger<ApproveClauseVersionCommandHandler> logger) : ICommandHandler<ApproveClauseVersionCommand>
{
    public async Task HandleAsync(ApproveClauseVersionCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ApproveClauses))
            throw new ForbiddenAccessException();
        var userId = currentUser.UserId!.Value;
        var clause = await ClauseReads.LoadAsync(db, command.ClauseId, cancellationToken);
        var version = clause.Versions.FirstOrDefault(v => v.Number == command.Number)
            ?? throw new NotFoundException(nameof(ClauseVersion), command.Number);
        if (version.AuthorId == userId)
            throw new ForbiddenAccessException("clause.ownWording", "Someone other than the author of this wording must approve it.");
        if (version.Status != ClauseStatus.Draft)
            throw new ConflictException("clause.notDraft", "Only a draft wording can be approved.");

        clause.Approve(command.Number, userId);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Clause {ClauseId} version {Number} approved by {UserId}", clause.Id, command.Number, userId);
    }
}

/// <summary>Withdraws a wording: still readable and cited by the contracts that used it, never offered again.</summary>
public record DeprecateClauseVersionCommand(Guid ClauseId, int Number) : ICommand;

public class DeprecateClauseVersionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : ICommandHandler<DeprecateClauseVersionCommand>
{
    public async Task HandleAsync(DeprecateClauseVersionCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ApproveClauses))
            throw new ForbiddenAccessException();
        var clause = await ClauseReads.LoadAsync(db, command.ClauseId, cancellationToken);
        if (clause.Versions.All(v => v.Number != command.Number))
            throw new NotFoundException(nameof(ClauseVersion), command.Number);
        clause.Deprecate(command.Number);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The Harvey rules a clause answers (R-M9-3).</summary>
public record LinkClauseRulesCommand(Guid ClauseId, List<string> RuleKeys) : ICommand;

public class LinkClauseRulesCommandValidator : AbstractValidator<LinkClauseRulesCommand>
{
    public LinkClauseRulesCommandValidator()
    {
        RuleFor(x => x.RuleKeys).NotNull().Must(k => k.Count <= 50);
        RuleForEach(x => x.RuleKeys).NotEmpty().MaximumLength(120);
    }
}

public class LinkClauseRulesCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : ICommandHandler<LinkClauseRulesCommand>
{
    public async Task HandleAsync(LinkClauseRulesCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.EditClauses))
            throw new ForbiddenAccessException();
        var clause = await ClauseReads.LoadAsync(db, command.ClauseId, cancellationToken);
        db.ClauseRuleLinks.AddRange(clause.LinkRules(command.RuleKeys));
        await db.SaveChangesAsync(cancellationToken);
    }
}
