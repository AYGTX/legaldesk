using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing.Commands;

public record SignFieldCommand(
    string SecureToken,
    Guid FieldId,
    SignatureType SignatureType,
    string? ImageBase64,
    string? TypedText,
    string? FontFamily,
    string IpAddress,
    string UserAgent,
    string? Location) : ICommand<SignFieldResult>;

public record SignFieldResult(Guid SignatureId, bool AllFieldsSigned, bool DocumentCompleted);

public class SignFieldCommandValidator : AbstractValidator<SignFieldCommand>
{
    public SignFieldCommandValidator()
    {
        RuleFor(x => x.SecureToken).NotEmpty();
        RuleFor(x => x.FieldId).NotEmpty();
        RuleFor(x => x.IpAddress).NotEmpty();
    }
}

public class SignFieldCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IAuditService auditService,
    ILogger<SignFieldCommandHandler> logger) : ICommandHandler<SignFieldCommand, SignFieldResult>
{
    public async Task<SignFieldResult> HandleAsync(SignFieldCommand command, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.SecureToken == command.SecureToken, cancellationToken)
            ?? throw new NotFoundException("Signing session not found or has expired.");

        if (recipient.Status == WorkflowStepStatus.Completed)
            throw new ConflictException("You have already completed signing.");

        if (recipient.Status == WorkflowStepStatus.Rejected)
            throw new ConflictException("You have rejected this document.");

        if (recipient.Document.IsExpired())
        {
            if (recipient.Document.Status == DocumentStatus.InProgress)
            {
                recipient.Document.SetStatus(DocumentStatus.Expired);
                await db.SaveChangesAsync(cancellationToken);
            }
            throw new ConflictException("This request has expired and can no longer be signed.");
        }

        // Parallel: all recipients sign simultaneously — no turn enforcement.
        // Sequential/Mixed: earlier stages (strictly lower Order) must complete first.
        // Same-Order recipients are in the same parallel stage and never block each other.
        var workflow = await db.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.DocumentId == recipient.DocumentId, cancellationToken);

        if (workflow?.Type != WorkflowType.Parallel)
        {
            var blockedByEarlier = await db.Recipients.AnyAsync(r =>
                r.DocumentId == recipient.DocumentId &&
                r.Order < recipient.Order &&
                r.IsRequired &&
                r.Status != WorkflowStepStatus.Completed &&
                r.Status != WorkflowStepStatus.Skipped, cancellationToken);
            if (blockedByEarlier)
                throw new ConflictException("It's not your turn yet — a previous signer must complete their part first.");
        }

        // Accept fields assigned directly to this recipient, plus orphaned fields whose
        // RecipientId doesn't correspond to any current recipient in the document (legacy
        // documents saved before the client-ID preservation fix).
        var field = await db.SignatureFields
            .FirstOrDefaultAsync(f =>
                f.Id == command.FieldId &&
                f.DocumentId == recipient.DocumentId &&
                (f.RecipientId == recipient.Id ||
                 f.RecipientId == null ||
                 !db.Recipients.Any(r => r.DocumentId == recipient.DocumentId && r.Id == f.RecipientId)),
                cancellationToken)
            ?? throw new NotFoundException(nameof(SignatureField), command.FieldId);

        if (field.IsSigned)
            throw new ConflictException("This field has already been signed.");

        string? imageUrl = null;
        if (!string.IsNullOrEmpty(command.ImageBase64))
        {
            var bytes = Convert.FromBase64String(command.ImageBase64.Split(',').Last());
            using var ms = new MemoryStream(bytes);
            imageUrl = await storage.UploadAsync(ms, $"sig_{command.FieldId}.png", "image/png", $"signatures/{recipient.DocumentId}", cancellationToken);
        }

        var signature = Signature.Create(
            field.Id,
            recipient.Id,
            command.SignatureType,
            command.IpAddress,
            command.UserAgent,
            imageUrl,
            command.TypedText,
            command.FontFamily,
            command.Location);

        field.SetSigned(command.TypedText ?? command.SignatureType.ToString());
        db.Signatures.Add(signature);

        recipient.MarkViewed();
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            recipient.DocumentId,
            AuditAction.FieldSigned,
            command.IpAddress,
            $"Field '{field.FieldType}' signed",
            recipientId: recipient.Id,
            userAgent: command.UserAgent,
            location: command.Location,
            actorName: recipient.Name,
            actorEmail: recipient.Email,
            cancellationToken: cancellationToken);

        var allSigned = await CheckAllFieldsSigned(recipient, cancellationToken);

        logger.LogInformation("Field {FieldId} signed by recipient {RecipientId}", field.Id, recipient.Id);

        // AdvanceAsync is deferred — the recipient must explicitly approve after reviewing all fields.
        return new SignFieldResult(signature.Id, allSigned, DocumentCompleted: false);
    }

    private async Task<bool> CheckAllFieldsSigned(Recipient recipient, CancellationToken cancellationToken)
    {
        var requiredFields = await db.SignatureFields
            .Where(f => f.DocumentId == recipient.DocumentId && f.IsRequired &&
                        (f.RecipientId == recipient.Id ||
                         f.RecipientId == null ||
                         !db.Recipients.Any(r => r.DocumentId == recipient.DocumentId && r.Id == f.RecipientId)))
            .ToListAsync(cancellationToken);

        return requiredFields.All(f => f.IsSigned);
    }
}
