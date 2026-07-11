using FluentValidation;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

public record DeleteDocumentCommand(Guid DocumentId) : ICommand;

public class DeleteDocumentCommandValidator : AbstractValidator<DeleteDocumentCommand>
{
    public DeleteDocumentCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
    }
}

public class DeleteDocumentCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    ICurrentUserService currentUser) : ICommandHandler<DeleteDocumentCommand>
{
    public async Task HandleAsync(DeleteDocumentCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FindAsync([command.DocumentId], cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (document.OwnerId != currentUser.UserId && !currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        if (document.Status == DocumentStatus.InProgress)
            throw new ConflictException("Cannot delete a document that is in progress.");

        document.SoftDelete();
        await db.SaveChangesAsync(cancellationToken);
    }
}
