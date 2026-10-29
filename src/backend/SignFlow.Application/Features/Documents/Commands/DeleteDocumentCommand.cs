using SignFlow.Application.Common.Security;
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

        if (document.OwnerId != currentUser.UserId && !currentUser.Can(Permissions.ManageAllContracts))
            throw new ForbiddenAccessException();

        // Only a draft can be deleted (R-M2-6): anything that went out for review or signature is evidence.
        if (document.Status != DocumentStatus.Draft)
            throw new ConflictException("Only a draft can be deleted.");

        document.SoftDelete();
        await db.SaveChangesAsync(cancellationToken);
    }
}
