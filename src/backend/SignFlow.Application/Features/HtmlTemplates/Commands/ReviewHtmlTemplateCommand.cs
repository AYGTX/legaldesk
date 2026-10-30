using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.Application.Features.HtmlTemplates.Commands;

public record ReviewTemplateField(string FieldKey, string Label, string Type, string Role);

/// <summary>The template as the editor holds it right now — possibly unsaved — so the review follows the edits.</summary>
public record ReviewHtmlTemplateCommand(string HtmlContent, List<ReviewTemplateField> Fields)
    : ICommand<ReviewHtmlTemplateResult>;

/// <summary>One risk in the template, with the rule and the clause it was found in.</summary>
public record TemplateFindingDto(
    string RuleKey,
    int? RuleVersion,
    string ClauseType,
    string Severity,
    string Explanation,
    string? RecommendedAction,
    string? ProposedLanguage,
    string? ContractReference,
    List<string> Quotes,
    List<ReviewArticle> Articles);

public record ReviewHtmlTemplateResult(int RiskScore, List<TemplateFindingDto> Findings, List<string> Limitations, string? GridVersion);

public class ReviewHtmlTemplateCommandValidator : AbstractValidator<ReviewHtmlTemplateCommand>
{
    public ReviewHtmlTemplateCommandValidator()
    {
        RuleFor(x => x.HtmlContent).NotEmpty().MaximumLength(200_000);
        RuleFor(x => x.Fields).NotNull().Must(f => f.Count <= 200).WithMessage("A template has at most 200 fields.");
    }
}

/// <summary>
/// Reviews a template before any request exists: the same Harvey review, with the approved rules and the
/// organisation's settings, that runs on a contract, applied to the template's wording. Placeholders read as «Label» gaps and every conditional
/// clause is included, so the review sees the whole contract the template can produce. Nothing is stored.
///
/// Like the pre-flight, it refuses to answer when Harvey could not run: an empty list of findings would read as
/// "no problems".
/// </summary>
public class ReviewHtmlTemplateCommandHandler(
    IApplicationDbContext db,
    IContractReviewService reviewService,
    ICurrentUserService currentUser) : ICommandHandler<ReviewHtmlTemplateCommand, ReviewHtmlTemplateResult>
{
    public async Task<ReviewHtmlTemplateResult> HandleAsync(ReviewHtmlTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageTemplates))
            throw new ForbiddenAccessException();

        // Checked here too: the validator above only runs when the pipeline runs it.
        if (string.IsNullOrWhiteSpace(command.HtmlContent) || command.HtmlContent.Length > 200_000)
            throw new Common.Exceptions.ValidationException("The template text is empty or too long to review.");
        if (command.Fields is null || command.Fields.Count > 200)
            throw new Common.Exceptions.ValidationException("A template has at most 200 fields.");

        var labels = command.Fields
            .Where(f => !string.Equals(f.Type, "Signature", StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.FieldKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().Label) ? g.Key : g.First().Label, StringComparer.Ordinal);

        var text = HtmlToText.Convert(HtmlContractRenderer.RenderForReview(command.HtmlContent, labels));

        var settings = await db.OrganisationRuleSettings.AsNoTracking().ToListAsync(cancellationToken);
        ContractReview review;
        try
        {
            review = await reviewService.ReviewAsync(
                text,
                [.. settings.Where(s => !s.Enabled).Select(s => s.RuleKey)],
                settings.Where(s => s.Enabled && s.SeverityOverride is not null).ToDictionary(s => s.RuleKey, s => s.SeverityOverride!),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ServiceUnavailableException(Domain.Entities.ReviewRun.Unavailable,
                "Harvey cannot be reached: the template has not been reviewed. Try again in a few minutes.");
        }

        var findings = review.Findings
            .Select(f => new TemplateFindingDto(f.RuleKey, f.RuleVersion, f.Label, f.Severity, f.Explanation,
                f.RecommendedAction, f.ProposedLanguage, f.ClauseReference, [.. f.Quotes], [.. f.Articles]))
            .ToList();
        var risk = Math.Min(100, review.Findings.Sum(f => ReviewGrading.Weight(f.Severity)));
        return new ReviewHtmlTemplateResult(risk, findings, [], review.GridVersion);
    }
}
