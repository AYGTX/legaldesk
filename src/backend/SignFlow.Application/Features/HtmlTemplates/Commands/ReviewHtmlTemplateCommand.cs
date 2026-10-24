using FluentValidation;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.Application.Features.HtmlTemplates.Commands;

public record ReviewTemplateField(string FieldKey, string Label, string Type, string Role);

/// <summary>The template as the editor holds it right now — possibly unsaved — so the review follows the edits.</summary>
public record ReviewHtmlTemplateCommand(string HtmlContent, List<ReviewTemplateField> Fields)
    : ICommand<ReviewHtmlTemplateResult>;

public record ReviewHtmlTemplateResult(int RiskScore, List<RiskFinding> Findings, List<string> Limitations);

public class ReviewHtmlTemplateCommandValidator : AbstractValidator<ReviewHtmlTemplateCommand>
{
    public ReviewHtmlTemplateCommandValidator()
    {
        RuleFor(x => x.HtmlContent).NotEmpty().MaximumLength(200_000);
        RuleFor(x => x.Fields).NotNull().Must(f => f.Count <= 200).WithMessage("A template has at most 200 fields.");
    }
}

/// <summary>
/// Reviews a template before any request exists: the same legal review (Harvey, grounded in the law corpus) that runs
/// on a finished contract, applied to the template's wording. Placeholders read as «Label» gaps and every conditional
/// clause is included, so the review sees the whole contract the template can produce. Nothing is stored.
///
/// Like the pre-flight, it refuses to answer when the review could not run: an empty list of findings would read as
/// "no problems".
/// </summary>
public class ReviewHtmlTemplateCommandHandler(
    IAiService aiService,
    ILegalPlaybookService playbook,
    ICurrentUserService currentUser) : ICommandHandler<ReviewHtmlTemplateCommand, ReviewHtmlTemplateResult>
{
    public async Task<ReviewHtmlTemplateResult> HandleAsync(ReviewHtmlTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
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

        var risk = await aiService.AnalyzeContractRiskAsync(text, "Template", cancellationToken);
        if (risk.ReviewFailed)
            throw new ConflictException(risk.Limitations?.FirstOrDefault()
                ?? "The contract review is unavailable; the template has not been reviewed.");

        risk = await playbook.AttachToRiskAsync(risk, text, cancellationToken);
        return new ReviewHtmlTemplateResult(risk.RiskScore, risk.Findings, risk.Limitations ?? []);
    }
}
