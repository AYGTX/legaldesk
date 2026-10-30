using SignFlow.IntegrationTests.Stubs;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Recipients.Commands;
using SignFlow.Application.Features.SignatureFields.Commands;
using SignFlow.Application.Features.Signing.Commands;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.IntegrationTests.Infrastructure;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// End-to-end scenario: owner uploads a PDF, places a signature field, sends it to a
/// recipient, and the recipient signs and then approves — verifying the document reaches
/// Completed. Signing a field deliberately does NOT advance the workflow: the recipient
/// reviews every field first and submits with an explicit approval, which is the step that
/// marks them Completed and hands off to the next signer.
/// Email is bypassed (NullEmailService); an in-memory database is used.
/// </summary>
public class SigningScenarioTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private const string OwnerEmail = "owner@test.com";
    private const string RecipientEmail = "signer@test.com";

    // Seeds the owner user into the in-memory DB (the FK on Document requires the row to exist).
    private static async Task SeedOwnerAsync(ScenarioFixture fixture)
    {
        var db = (Microsoft.EntityFrameworkCore.DbContext)fixture.Db;
        db.Set<ApplicationUser>().Add(new ApplicationUser
        {
            Id = OwnerId,
            Email = OwnerEmail,
            UserName = OwnerEmail,
            FirstName = "Test",
            LastName = "Owner",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await fixture.Db.SaveChangesAsync(default);
    }

    [Fact]
    public async Task Owner_uploads_places_field_sends__recipient_signs__document_completes()
    {
        using var fixture = new ScenarioFixture(OwnerId, OwnerEmail);
        await SeedOwnerAsync(fixture);

        // ── 1. Upload document ────────────────────────────────────────────────
        var pdfBytes = NullPdfInspector.FakePdf; // passes the %PDF- signature check
        using var pdfStream = new MemoryStream(pdfBytes);

        var uploadHandler = fixture.GetHandler<UploadDocumentCommandHandler>();
        var uploadResult = await uploadHandler.HandleAsync(new UploadDocumentCommand(
            FileStream: pdfStream,
            FileName: "contract.pdf",
            FileSizeBytes: pdfBytes.Length,
            ContentType: "application/pdf",
            Title: "Service Contract"));

        var documentId = uploadResult.DocumentId;
        Assert.NotEqual(Guid.Empty, documentId);

        // ── 2. Add recipient ──────────────────────────────────────────────────
        var recipientsHandler = fixture.GetHandler<ManageRecipientsCommandHandler>();
        await recipientsHandler.HandleAsync(new ManageRecipientsCommand(
            DocumentId: documentId,
            WorkflowType: WorkflowType.Sequential,
            Recipients:
            [
                new RecipientInputDto(
                    Id: null,
                    Name: "Alice Signer",
                    Email: RecipientEmail,
                    Order: 1,
                    ApprovalType: ApprovalType.Signature)
            ]));

        var recipient = await fixture.Db.Recipients
            .FirstAsync(r => r.DocumentId == documentId);

        // ── 3. Place a signature field assigned to the recipient ──────────────
        var fieldsHandler = fixture.GetHandler<SaveFieldsCommandHandler>();
        await fieldsHandler.HandleAsync(new SaveFieldsCommand(
            DocumentId: documentId,
            Fields:
            [
                new FieldDto(
                    Id: null,
                    FieldType: FieldType.Signature,
                    Page: 1,
                    X: 100f, Y: 200f, Width: 150f, Height: 50f,
                    Rotation: null,
                    RecipientId: recipient.Id,
                    Label: "Signature",
                    Placeholder: null, DefaultValue: null, FontFamily: null, FontSize: null,
                    FontColor: null, BorderColor: null, BackgroundColor: null,
                    IsRequired: true)
            ]));

        var field = await fixture.Db.SignatureFields
            .FirstAsync(f => f.DocumentId == documentId);

        // ── 4. Send document (starts workflow) ────────────────────────────────
        var sendHandler = fixture.GetHandler<SendDocumentCommandHandler>();
        await fixture.ReviewAsync(documentId); // nothing goes out unreviewed (R-M8-8)
        await sendHandler.HandleAsync(new SendDocumentCommand(documentId));

        var sentDoc = await fixture.Db.Documents.FindAsync(documentId);
        Assert.Equal(DocumentStatus.InProgress, sentDoc!.Status);

        // ── 5. Recipient signs the field ──────────────────────────────────────
        // Acting on a request starts with confirming the document (and its review) was read.
        recipient.AcknowledgeReview();
        await fixture.Db.SaveChangesAsync(default);

        var signHandler = fixture.GetHandler<SignFieldCommandHandler>();
        var signResult = await signHandler.HandleAsync(new SignFieldCommand(
            SecureToken: recipient.SecureToken,
            FieldId: field.Id,
            SignatureType: SignatureType.Typed,
            ImageBase64: null,
            TypedText: "Alice Signer",
            FontFamily: "Arial",
            IpAddress: "127.0.0.1",
            UserAgent: "IntegrationTest/1.0",
            Location: null));

        Assert.True(signResult.AllFieldsSigned, "All fields should be signed after the only field is signed.");
        Assert.False(signResult.DocumentCompleted, "Signing a field must not complete the document on its own.");

        var midDoc = await fixture.Db.Documents.FindAsync(documentId);
        Assert.Equal(DocumentStatus.InProgress, midDoc!.Status);

        // ── 6. Recipient approves, which advances the workflow ────────────────
        var approveHandler = fixture.GetHandler<ApproveSigningCommandHandler>();
        var approveResult = await approveHandler.HandleAsync(new ApproveSigningCommand(
            SecureToken: recipient.SecureToken,
            IpAddress: "127.0.0.1",
            UserAgent: "IntegrationTest/1.0"));

        // ── 7. Assertions ─────────────────────────────────────────────────────
        Assert.True(approveResult.DocumentCompleted, "Document should complete when the sole recipient approves.");

        var completedDoc = await fixture.Db.Documents.FindAsync(documentId);
        Assert.Equal(DocumentStatus.Completed, completedDoc!.Status);
        Assert.NotNull(completedDoc.CompletedAt);

        var signedField = await fixture.Db.SignatureFields.FindAsync(field.Id);
        Assert.True(signedField!.IsSigned);

        var signature = await fixture.Db.Signatures
            .FirstOrDefaultAsync(s => s.SignatureFieldId == field.Id);
        Assert.NotNull(signature);
        Assert.Equal(SignatureType.Typed, signature.SignatureType);
        Assert.Equal("Alice Signer", signature.TypedText);
    }

    [Fact]
    public async Task Sequential_workflow__second_recipient_cannot_sign_before_first()
    {
        using var fixture = new ScenarioFixture(OwnerId, OwnerEmail);
        await SeedOwnerAsync(fixture);

        using var pdfStream = new MemoryStream(NullPdfInspector.FakePdf);
        var uploadHandler = fixture.GetHandler<UploadDocumentCommandHandler>();
        var uploadResult = await uploadHandler.HandleAsync(new UploadDocumentCommand(
            pdfStream, "doc.pdf", 4, "application/pdf", "Two-party Contract"));

        var docId = uploadResult.DocumentId;

        // Two recipients
        var recipientsHandler = fixture.GetHandler<ManageRecipientsCommandHandler>();
        await recipientsHandler.HandleAsync(new ManageRecipientsCommand(docId, WorkflowType.Sequential,
        [
            new(null, "Alice", "alice@test.com", 1, ApprovalType.Signature),
            new(null, "Bob",   "bob@test.com",   2, ApprovalType.Signature)
        ]));

        var alice = await fixture.Db.Recipients.FirstAsync(r => r.Email == "alice@test.com");
        var bob   = await fixture.Db.Recipients.FirstAsync(r => r.Email == "bob@test.com");

        // One field per recipient
        var fieldsHandler = fixture.GetHandler<SaveFieldsCommandHandler>();
        await fieldsHandler.HandleAsync(new SaveFieldsCommand(docId,
        [
            new(null, FieldType.Signature, 1, 10f, 10f, 100f, 40f, null, alice.Id, null, null, null, null, null, null, null, null),
            new(null, FieldType.Signature, 1, 10f, 60f, 100f, 40f, null, bob.Id,   null, null, null, null, null, null, null, null)
        ]));

        var aliceField = await fixture.Db.SignatureFields.FirstAsync(f => f.RecipientId == alice.Id);
        var bobField   = await fixture.Db.SignatureFields.FirstAsync(f => f.RecipientId == bob.Id);

        await fixture.ReviewAsync(docId);
        await fixture.GetHandler<SendDocumentCommandHandler>().HandleAsync(new SendDocumentCommand(docId));

        var signHandler = fixture.GetHandler<SignFieldCommandHandler>();
        var approveHandler = fixture.GetHandler<ApproveSigningCommandHandler>();

        alice.AcknowledgeReview();
        bob.AcknowledgeReview();
        await fixture.Db.SaveChangesAsync(default);

        // Bob tries to sign before Alice — must be rejected
        await Assert.ThrowsAsync<SignFlow.Application.Common.Exceptions.ConflictException>(
            () => signHandler.HandleAsync(new SignFieldCommand(
                bob.SecureToken, bobField.Id, SignatureType.Typed,
                null, "Bob", null, "127.0.0.1", "test", null)));

        // Alice signs her field
        var aliceResult = await signHandler.HandleAsync(new SignFieldCommand(
            alice.SecureToken, aliceField.Id, SignatureType.Typed,
            null, "Alice", null, "127.0.0.1", "test", null));

        Assert.True(aliceResult.AllFieldsSigned);

        // Alice has signed but not approved, so her step is still open and Bob's turn hasn't
        // arrived — the gate is the approval, not the signature.
        await Assert.ThrowsAsync<SignFlow.Application.Common.Exceptions.ConflictException>(
            () => signHandler.HandleAsync(new SignFieldCommand(
                bob.SecureToken, bobField.Id, SignatureType.Typed,
                null, "Bob", null, "127.0.0.1", "test", null)));

        var aliceApproval = await approveHandler.HandleAsync(new ApproveSigningCommand(
            alice.SecureToken, "127.0.0.1", "test"));
        Assert.False(aliceApproval.DocumentCompleted, "Document should not complete until Bob also approves.");

        // Now Bob can sign and approve
        var bobResult = await signHandler.HandleAsync(new SignFieldCommand(
            bob.SecureToken, bobField.Id, SignatureType.Typed,
            null, "Bob", null, "127.0.0.1", "test", null));

        Assert.True(bobResult.AllFieldsSigned);

        var bobApproval = await approveHandler.HandleAsync(new ApproveSigningCommand(
            bob.SecureToken, "127.0.0.1", "test"));
        Assert.True(bobApproval.DocumentCompleted);

        var doc = await fixture.Db.Documents.FindAsync(docId);
        Assert.Equal(DocumentStatus.Completed, doc!.Status);
    }
}
