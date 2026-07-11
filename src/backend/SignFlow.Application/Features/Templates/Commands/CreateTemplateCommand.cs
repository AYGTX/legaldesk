using FluentValidation;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Templates.Commands;

public record CreateTemplateCommand(
    Stream FileStream,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    string Name,
    string? Description = null,
    string? Category = null,
    bool IsPublic = false) : ICommand<Guid>;

public class CreateTemplateCommandValidator : AbstractValidator<CreateTemplateCommand>
{
    public CreateTemplateCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(500);
        RuleFor(x => x.FileName).NotEmpty().Must(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.FileSizeBytes).GreaterThan(0).LessThanOrEqualTo(100 * 1024 * 1024);
    }
}

public class CreateTemplateCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    ICurrentUserService currentUser) : ICommandHandler<CreateTemplateCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateTemplateCommand command, CancellationToken cancellationToken = default)
    {
        var pageCount = await pdfService.GetPageCountAsync(command.FileStream, cancellationToken);
        command.FileStream.Position = 0;

        var userId = currentUser.UserId!.Value;
        var storagePath = await storage.UploadAsync(command.FileStream, command.FileName, command.ContentType, $"templates/{userId}", cancellationToken);

        var template = Template.Create(command.Name, storagePath, command.FileName, command.FileSizeBytes, pageCount, userId, command.Description);
        if (command.IsPublic) template.SetPublic(true);
        template.SetCreatedBy(userId.ToString());

        db.Templates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        return template.Id;
    }
}
