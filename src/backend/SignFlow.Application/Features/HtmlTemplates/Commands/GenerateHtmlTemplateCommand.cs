using FluentValidation;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.HtmlTemplates.Commands;

public record GenerateHtmlTemplateCommand(string UserText) : ICommand<GenerateHtmlTemplateResult>;

public record GenerateHtmlTemplateFieldResult(
    string FieldKey,
    string Label,
    string Type,
    string Role,
    bool IsRequired,
    int Order,
    string? AutoFillFrom);

public record GenerateHtmlTemplateResult(
    string HtmlContent,
    string SuggestedName,
    List<GenerateHtmlTemplateFieldResult> Fields);

public class GenerateHtmlTemplateCommandValidator : AbstractValidator<GenerateHtmlTemplateCommand>
{
    public GenerateHtmlTemplateCommandValidator()
    {
        RuleFor(x => x.UserText).NotEmpty().MaximumLength(8000);
    }
}

public class GenerateHtmlTemplateCommandHandler(
    IAiService aiService,
    ICurrentUserService currentUser) : ICommandHandler<GenerateHtmlTemplateCommand, GenerateHtmlTemplateResult>
{
    public async Task<GenerateHtmlTemplateResult> HandleAsync(GenerateHtmlTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        var result = await aiService.GenerateHtmlFromTextAsync(command.UserText, cancellationToken);

        return new GenerateHtmlTemplateResult(
            result.HtmlContent,
            result.SuggestedName,
            result.Fields.Select(f => new GenerateHtmlTemplateFieldResult(
                f.FieldKey, f.Label, f.Type, f.Role, f.IsRequired, f.Order, f.AutoFillFrom)).ToList());
    }
}
