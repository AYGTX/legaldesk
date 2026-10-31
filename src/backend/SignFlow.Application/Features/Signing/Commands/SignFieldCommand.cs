using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing.Commands;

public record SignFieldCommand(
    Guid RecipientId,
    Guid FieldId,
    SignatureType SignatureType,
    string? ImageBase64,
    string? TypedText,
    string? FontFamily,
    string IpAddress,
    string UserAgent,
    SigningEvidence? Evidence = null) : ICommand<SignFieldResult>;

/// <summary>How the participant proved who they are, from their signing session (R-M5-5).</summary>
public record SigningEvidence(string? VerifiedEmail, string? Method, DateTime? VerifiedAt);

public record SignFieldResult(Guid SignatureId, bool AllFieldsSigned, bool DocumentCompleted);

public class SignFieldCommandValidator : AbstractValidator<SignFieldCommand>
{
    public SignFieldCommandValidator()
    {
        RuleFor(x => x.RecipientId).NotEmpty();
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
            .FirstOrDefaultAsync(r => r.Id == command.RecipientId, cancellationToken)
            ?? throw new NotFoundException("Signing session not found or has expired.");

        if (recipient.Status == WorkflowStepStatus.Completed)
            throw new ConflictException("You have already completed signing.");

        if (recipient.Status == WorkflowStepStatus.Rejected)
            throw new ConflictException("You have rejected this document.");

        if (recipient.ApprovalType == ApprovalType.ViewOnly)
            throw new ConflictException("View-only participants cannot sign fields.");

        ReviewGateRules.Require(recipient);

        if (recipient.Document.Status == DocumentStatus.ChangesRequested)
            throw new ConflictException("Changes have been requested on this document — it is paused until the owner uploads a revision.");

        // Post-revision the document sits in Draft while the owner re-places fields, but the
        // signing links are already circulating — block signing until the explicit re-send.
        if (recipient.Document.Status == DocumentStatus.Draft)
            throw new ConflictException("This document is being revised by its owner and is not open for signing yet.");

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

        // Accept fields assigned directly to this recipient, plus — for content signers only —
        // orphaned fields whose RecipientId doesn't correspond to any current recipient in the
        // document (legacy documents saved before the client-ID preservation fix). Reviewers and
        // approvers never claim orphans: their role is a gate, not field completion.
        var canClaimOrphans = recipient.ApprovalType is ApprovalType.Signature or ApprovalType.Stamp;
        var field = await db.SignatureFields
            .FirstOrDefaultAsync(f =>
                f.Id == command.FieldId &&
                f.DocumentId == recipient.DocumentId &&
                (f.RecipientId == recipient.Id ||
                 (canClaimOrphans &&
                  (f.RecipientId == null ||
                   !db.Recipients.Any(r => r.DocumentId == recipient.DocumentId && r.Id == f.RecipientId)))),
                cancellationToken)
            ?? throw new NotFoundException(nameof(SignatureField), command.FieldId);

        if (field.IsSigned)
            throw new ConflictException("This field has already been signed.");

        // Radio groups are single-choice: selecting one option voids any previously selected
        // option in the same group. The old Signature rows stay as evidence (same policy as
        // revision voiding); IsSigned/SignedValue move to the new choice, and stamping only
        // renders signed fields, so exactly one option lands on the final PDF.
        if (field.FieldType == FieldType.Radio && !string.IsNullOrEmpty(field.GroupName))
        {
            var previousChoices = await db.SignatureFields
                .Where(f => f.DocumentId == recipient.DocumentId
                         && f.Id != field.Id
                         && f.FieldType == FieldType.Radio
                         && f.GroupName == field.GroupName
                         && f.RecipientId == field.RecipientId
                         && f.IsSigned)
                .ToListAsync(cancellationToken);
            foreach (var previous in previousChoices)
                previous.VoidSignature();
        }

        // A drawn signature is checked and re-encoded by the server before it is stored (R-M5-6).
        string? imageUrl = null, imageSha256 = null;
        if (!string.IsNullOrEmpty(command.ImageBase64))
        {
            var (png, sha) = SignatureImage.Sanitize(command.ImageBase64);
            using var ms = new MemoryStream(png);
            imageUrl = await storage.UploadAsync(ms, $"sig_{command.FieldId}.png", "image/png", $"signatures/{recipient.DocumentId}", cancellationToken);
            imageSha256 = sha;
        }

        // A date is the server's, in the organisation's time zone and format: the signer cannot type it (R-M5-7).
        var value = command.TypedText ?? command.SignatureType.ToString();
        if (field.FieldType == FieldType.Date)
        {
            var organisation = await db.Organisations.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == recipient.OrganisationId, cancellationToken);
            value = SigningDates.Today(organisation?.TimeZoneId, organisation?.DateFormat);
        }

        var signature = Signature.Create(
            field.Id,
            recipient.Id,
            command.SignatureType,
            command.IpAddress,
            command.UserAgent,
            imageUrl,
            field.FieldType == FieldType.Date ? value : command.TypedText,
            command.FontFamily);
        signature.RecordEvidence(command.Evidence?.VerifiedEmail, command.Evidence?.Method, command.Evidence?.VerifiedAt,
            recipient.Document.Version, DocumentFingerprint.Of(recipient.Document), imageSha256);

        field.SetSigned(value);
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
        // Fetch ALL of the recipient's fields (not just required ones): a required radio group
        // is satisfied by any signed member, including optional ones.
        var canClaimOrphans = recipient.ApprovalType is ApprovalType.Signature or ApprovalType.Stamp;
        var fields = await db.SignatureFields
            .Where(f => f.DocumentId == recipient.DocumentId &&
                        (f.RecipientId == recipient.Id ||
                         (canClaimOrphans &&
                          (f.RecipientId == null ||
                           !db.Recipients.Any(r => r.DocumentId == recipient.DocumentId && r.Id == f.RecipientId)))))
            .ToListAsync(cancellationToken);

        return RequiredFieldRules.AllRequiredSatisfied(fields);
    }
}
