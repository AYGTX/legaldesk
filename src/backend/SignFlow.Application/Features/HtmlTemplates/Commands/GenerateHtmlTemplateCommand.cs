using SignFlow.Application.Common.Security;
using FluentValidation;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

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
    ICurrentUserService currentUser,
    ModelAccess model) : ICommandHandler<GenerateHtmlTemplateCommand, GenerateHtmlTemplateResult>
{
    public async Task<GenerateHtmlTemplateResult> HandleAsync(GenerateHtmlTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageTemplates))
            throw new ForbiddenAccessException();
        // The pasted text is a model's input like a contract's: only when the organisation turned the assistant on (R-M12-1).
        await model.RequireOnAsync(cancellationToken);

        var result = await aiService.GenerateHtmlFromTextAsync(command.UserText, cancellationToken);

        return new GenerateHtmlTemplateResult(
            result.HtmlContent,
            result.SuggestedName,
            result.Fields.Select(f => new GenerateHtmlTemplateFieldResult(
                f.FieldKey, f.Label, f.Type, f.Role, f.IsRequired, f.Order, f.AutoFillFrom)).ToList());
    }
}
