using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Templates;

/// <summary>Which of the two template kinds a lifecycle command acts on.</summary>
public enum TemplateKind
{
    Html = 0,
    Pdf = 1
}

/// <summary>Where a template version stands, as the lifecycle commands return it.</summary>
public record TemplateVersionDto(Guid Id, TemplateKind Kind, int Version, TemplateStatus Status, Guid? ParentTemplateId);

/// <summary>A draft goes to legal review (R-M6-5). Editing it afterwards brings it back to draft.</summary>
public record SubmitTemplateCommand(TemplateKind Kind, Guid Id) : ICommand<TemplateVersionDto>;

/// <summary>
/// Approves a template version in legal review (R-M6-5): a Manager or an Administrator, after Harvey has reviewed the
/// wording; an open Critical finding refuses the approval. Approving retires the version it replaces.
/// </summary>
public record ApproveTemplateCommand(TemplateKind Kind, Guid Id) : ICommand<TemplateVersionDto>;

/// <summary>Withdraws a version: no new contract is made from it; contracts already made keep naming it.</summary>
public record RetireTemplateCommand(TemplateKind Kind, Guid Id) : ICommand<TemplateVersionDto>;

/// <summary>Drafts the next version of an approved template: the approved one stays in use until the draft is approved.</summary>
public record DraftNextTemplateVersionCommand(TemplateKind Kind, Guid Id) : ICommand<TemplateVersionDto>;

internal static class TemplateRows
{
    internal static async Task<VersionedTemplate> LoadAsync(IApplicationDbContext db, TemplateKind kind, Guid id, CancellationToken ct) => kind switch
    {
        TemplateKind.Html => await db.HtmlTemplates.Include(t => t.Fields).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(HtmlTemplate), id),
        _ => await db.PdfTemplates.Include(t => t.Fields).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(PdfTemplate), id)
    };

    internal static TemplateVersionDto ToDto(VersionedTemplate t) => new(
        t.Id, t is HtmlTemplate ? TemplateKind.Html : TemplateKind.Pdf, t.Version, t.Status, t.ParentTemplateId);
}

public class SubmitTemplateCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : ICommandHandler<SubmitTemplateCommand, TemplateVersionDto>
{
    public async Task<TemplateVersionDto> HandleAsync(SubmitTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageTemplates))
            throw new ForbiddenAccessException();
        var template = await TemplateRows.LoadAsync(db, command.Kind, command.Id, cancellationToken);
        if (template.Status != TemplateStatus.Draft)
            throw new ConflictException("template.notDraft", "Only a draft is sent to legal review.");
        template.SubmitForReview(currentUser.UserId!.Value);
        await db.SaveChangesAsync(cancellationToken);
        return TemplateRows.ToDto(template);
    }
}

public class ApproveTemplateCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IContractReviewService reviewService,
    IStorageService storage,
    IPdfService pdf,
    ILogger<ApproveTemplateCommandHandler> logger) : ICommandHandler<ApproveTemplateCommand, TemplateVersionDto>
{
    public async Task<TemplateVersionDto> HandleAsync(ApproveTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ApproveTemplates))
            throw new ForbiddenAccessException();
        var template = await TemplateRows.LoadAsync(db, command.Kind, command.Id, cancellationToken);
        if (template.Status != TemplateStatus.InLegalReview)
            throw new ConflictException("template.notInReview", "Send the template to legal review before approving it.");

        // Harvey reviews the wording the template produces before anyone may approve it.
        var review = await ReviewAsync(template, cancellationToken);
        var critical = review.Findings.Count(f => string.Equals(f.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        if (critical > 0)
            throw new ConflictException("template.criticalFindings",
                $"Harvey found {critical} critical issue(s) in this template; fix them before approving it.",
                new Dictionary<string, object?> { ["count"] = critical });

        var userId = currentUser.UserId!.Value;
        template.Approve(userId);

        // The version it replaces makes no new contract from now on.
        if (template.ParentTemplateId is { } parentId)
        {
            VersionedTemplate? parent = template is HtmlTemplate
                ? await db.HtmlTemplates.FirstOrDefaultAsync(t => t.Id == parentId, cancellationToken)
                : await db.PdfTemplates.FirstOrDefaultAsync(t => t.Id == parentId, cancellationToken);
            if (parent is { Status: TemplateStatus.Approved }) parent.Retire();
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Template {Kind} {Id} v{Version} approved by {UserId} ({Findings} finding(s), none critical)",
            command.Kind, template.Id, template.Version, userId, review.Findings.Count);
        return TemplateRows.ToDto(template);
    }

    private async Task<ContractReview> ReviewAsync(VersionedTemplate template, CancellationToken ct)
    {
        var text = template switch
        {
            HtmlTemplate html => HtmlToText.Convert(HtmlContractRenderer.RenderForReview(html.HtmlContent,
                html.Fields.Where(f => f.Type != HtmlTemplateFieldType.Signature)
                    .GroupBy(f => f.FieldKey).ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().Label) ? g.Key : g.First().Label))),
            PdfTemplate p => await PdfTextAsync(p, ct),
            _ => string.Empty
        };

        var settings = await db.OrganisationRuleSettings.AsNoTracking().ToListAsync(ct);
        ContractReview review;
        try
        {
            review = await reviewService.ReviewAsync(
                text,
                [.. settings.Where(s => !s.Enabled).Select(s => s.RuleKey)],
                settings.Where(s => s.Enabled && s.SeverityOverride is not null).ToDictionary(s => s.RuleKey, s => s.SeverityOverride!),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ServiceUnavailableException(ReviewRun.Unavailable,
                "Harvey cannot be reached: the template cannot be approved without its review. Try again in a few minutes.");
        }
        if (review.RulesApplied == 0)
            throw new ConflictException(ReviewRun.NoRules,
                "No review rule is in service: a lawyer has to approve Harvey's rules before templates can be approved.");
        return review;
    }

    /// <summary>The PDF's words, then each blank as «Label», so the review sees what a contract will say and leave open.</summary>
    private async Task<string> PdfTextAsync(PdfTemplate template, CancellationToken ct)
    {
        await using var file = await storage.DownloadAsync(template.StoragePath, ct);
        var text = await pdf.ExtractTextAsync(file, ct);
        var blanks = template.Fields.Where(f => f.Type != PdfFieldType.Signature).OrderBy(f => f.Order).Select(f => $"{f.Label} : «{f.Label}»");
        return text + "\n\n" + string.Join("\n", blanks);
    }
}

public class RetireTemplateCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : ICommandHandler<RetireTemplateCommand, TemplateVersionDto>
{
    public async Task<TemplateVersionDto> HandleAsync(RetireTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ApproveTemplates))
            throw new ForbiddenAccessException();
        var template = await TemplateRows.LoadAsync(db, command.Kind, command.Id, cancellationToken);
        template.Retire();
        await db.SaveChangesAsync(cancellationToken);
        return TemplateRows.ToDto(template);
    }
}

public class DraftNextTemplateVersionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : ICommandHandler<DraftNextTemplateVersionCommand, TemplateVersionDto>
{
    public async Task<TemplateVersionDto> HandleAsync(DraftNextTemplateVersionCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageTemplates))
            throw new ForbiddenAccessException();
        var template = await TemplateRows.LoadAsync(db, command.Kind, command.Id, cancellationToken);
        if (template.Status is not (TemplateStatus.Approved or TemplateStatus.Retired))
            throw new ConflictException("template.editable", "This version is still a draft: edit it directly.");

        // One draft at a time: a second "new version" opens the one already started.
        var existing = command.Kind == TemplateKind.Html
            ? await db.HtmlTemplates.Where(t => t.ParentTemplateId == template.Id && (t.Status == TemplateStatus.Draft || t.Status == TemplateStatus.InLegalReview)).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken)
            : await db.PdfTemplates.Where(t => t.ParentTemplateId == template.Id && (t.Status == TemplateStatus.Draft || t.Status == TemplateStatus.InLegalReview)).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is { } openId)
            return TemplateRows.ToDto(await TemplateRows.LoadAsync(db, command.Kind, openId, cancellationToken));

        var userId = currentUser.UserId!.Value;
        VersionedTemplate next;
        if (template is HtmlTemplate html)
        {
            var copy = html.NextVersion(userId);
            db.HtmlTemplates.Add(copy);
            foreach (var field in html.Fields) db.HtmlTemplateFields.Add(field.CopyTo(copy.Id));
            next = copy;
        }
        else
        {
            var source = (PdfTemplate)template;
            var copy = source.NextVersion(userId);
            db.PdfTemplates.Add(copy);
            foreach (var field in source.Fields) db.PdfTemplateFields.Add(field.CopyTo(copy.Id));
            next = copy;
        }
        next.SetCreatedBy(userId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TemplateRows.ToDto(next);
    }
}
