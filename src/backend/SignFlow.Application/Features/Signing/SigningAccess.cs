using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing;

/// <summary>A signing request refused with a code the signing page explains (R-M5-2).</summary>
public sealed class SigningRefusedException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

/// <summary>What a signing endpoint needs.</summary>
public enum SigningNeed
{
    /// <summary>A valid link: the introduction and the e-mailed code.</summary>
    Link,
    /// <summary>A signing session: reading the contract, its review and comments.</summary>
    View,
    /// <summary>A session, an open contract and the participant's turn: signing and decisions.</summary>
    Act
}

/// <param name="Session">Null only for <see cref="SigningNeed.Link"/>.</param>
public sealed record SigningContext(Recipient Recipient, Document Document, SigningSession? Session);

public sealed record OpenedSigningSession(string Secret, DateTime ExpiresAt);

/// <summary>What the link shows before the code: enough to know what is being asked, nothing of the contract.</summary>
public sealed record SigningIntroDto(string DocumentTitle, string SenderName, string ParticipantName, string MaskedEmail,
    ApprovalType ApprovalType, DateTime? LinkExpiresAt);

public interface ISigningAccess
{
    Task<SigningContext> ResolveAsync(string token, string? sessionSecret, SigningNeed need, CancellationToken ct = default);
    Task<SigningIntroDto> IntroAsync(string token, CancellationToken ct = default);
    Task SendCodeAsync(string token, CancellationToken ct = default);
    Task<OpenedSigningSession> VerifyCodeAsync(string token, string code, string? ip, CancellationToken ct = default);
    Task<OpenedSigningSession> OpenForAccountAsync(Guid recipientId, string? ip, CancellationToken ct = default);
}

/// <summary>
/// The one gate in front of every signing endpoint (R-M5-1 to R-M5-4). The e-mailed link (stored as a hash) shows who
/// is asking and sends a 6-digit code; the code, or the participant's own LegalDesk account, opens a 30-minute session,
/// and only a session acts. Each refusal carries a code: link.invalid, link.expired, link.delegated, session.required,
/// session.expired, contract.expired, contract.closed, participant.done, step.not_open.
/// </summary>
public class SigningAccess(
    IApplicationDbContext db,
    IEmailService email,
    ICurrentUserService currentUser,
    IAuditService audit,
    ICircuitDirectory? directory = null) : ISigningAccess
{
    /// <summary>Request header carrying the signing session's secret.</summary>
    public const string SessionHeader = "X-Signing-Session";

    /// <summary>Route token used by account sessions, which have no link.</summary>
    public const string SessionRoute = "session";

    private static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(60);
    private const int CodesPerHour = 5;

    public async Task<SigningContext> ResolveAsync(string token, string? sessionSecret, SigningNeed need, CancellationToken ct = default)
    {
        SigningSession? session = null;
        Recipient? recipient;

        if (!string.IsNullOrWhiteSpace(sessionSecret))
        {
            var secretHash = SecureTokens.Hash(sessionSecret);
            session = await db.SigningSessions.FirstOrDefaultAsync(s => s.SecretHash == secretHash, ct);
            if (session is null || session.IsExpired)
                throw new SigningRefusedException("session.expired", 401, "Your signing session has ended. Confirm your e-mail address again.");

            recipient = await LoadAsync(r => r.Id == session.RecipientId, ct);
            // A session opened from one link does not act through another participant's link.
            if (recipient is not null && token != SessionRoute && recipient.TokenHash != SecureTokens.Hash(token))
                recipient = null;
        }
        else
        {
            if (token == SessionRoute)
                throw new SigningRefusedException("session.required", 401, "Confirm your e-mail address to continue.");
            var tokenHash = SecureTokens.Hash(token);
            recipient = await LoadAsync(r => r.TokenHash == tokenHash, ct);
        }

        if (recipient is null || recipient.Document.IsDeleted)
            throw new SigningRefusedException("link.invalid", 404, "This signing link is not valid.");

        // Handed to someone else (R-M4-8): the stand-in has their own link now.
        if (recipient.Status == WorkflowStepStatus.Delegated)
            throw new SigningRefusedException("link.delegated", 410, "You handed this step to someone else; your link no longer works.");

        // An account session does not depend on the e-mailed link; everything else does.
        if (session?.Method != SigningSession.Account && recipient.LinkExpired)
            throw new SigningRefusedException("link.expired", 410, "This signing link has expired. Ask the sender for a new one.");

        if (need >= SigningNeed.View && session is null)
            throw new SigningRefusedException("session.required", 401, "Confirm your e-mail address to continue.");

        if (need == SigningNeed.Act)
            await EnsureOpenAsync(recipient, ct);

        if (session is not null)
        {
            session.Touch();
            await db.SaveChangesAsync(ct);
        }
        return new SigningContext(recipient, recipient.Document, session);
    }

    private async Task EnsureOpenAsync(Recipient recipient, CancellationToken ct)
    {
        var document = recipient.Document;
        if (document.IsExpired())
            throw new SigningRefusedException("contract.expired", 410, "This request has expired and can no longer be signed.");

        if (document.Status != DocumentStatus.InProgress)
            throw new SigningRefusedException("contract.closed", 409, document.Status switch
            {
                DocumentStatus.Draft => "The sender is revising this contract; it is not open for signing yet.",
                DocumentStatus.ChangesRequested => "Changes were requested on this contract; it is paused until the sender revises it.",
                DocumentStatus.Completed => "This contract is already signed by everyone.",
                _ => "This contract is no longer open for signing."
            });

        if (recipient.Status is WorkflowStepStatus.Completed or WorkflowStepStatus.Rejected
            or WorkflowStepStatus.ChangesRequested or WorkflowStepStatus.Delegated or WorkflowStepStatus.Skipped)
            throw new SigningRefusedException("participant.done", 409, "You have already responded to this contract.");

        // Their turn is an open step in the current round (R-M4-2): the stage before theirs is done.
        var open = await db.WorkflowSteps.AnyAsync(s => s.RecipientId == recipient.Id && s.Status == WorkflowStepStatus.InProgress &&
            db.ContractRounds.Any(r => r.Id == s.RoundId && r.Outcome == RoundOutcome.Open), ct);
        if (!open)
            throw new SigningRefusedException("step.not_open", 409, "It is not your turn yet: an earlier participant has to respond first.");
    }

    public async Task<SigningIntroDto> IntroAsync(string token, CancellationToken ct = default)
    {
        var context = await ResolveAsync(token, null, SigningNeed.Link, ct);
        var sender = await db.Users.AsNoTracking()
            .Where(u => u.Id == context.Document.OwnerId)
            .Select(u => (u.FirstName + " " + u.LastName).Trim())
            .FirstOrDefaultAsync(ct);
        var r = context.Recipient;
        return new SigningIntroDto(context.Document.Title, sender ?? "LegalDesk", r.Name, Mask(r.Email), r.ApprovalType, r.TokenExpiresAt);
    }

    public async Task SendCodeAsync(string token, CancellationToken ct = default)
    {
        var context = await ResolveAsync(token, null, SigningNeed.Link, ct);
        var recipient = context.Recipient;

        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.SigningOtps
            .Where(o => o.RecipientId == recipient.Id && o.CreatedAt >= since)
            .Select(o => o.CreatedAt)
            .ToListAsync(ct);
        if (recent.Count > 0 && DateTime.UtcNow - recent.Max() < ResendDelay)
            throw new SigningRefusedException("code.too_soon", 429, "A code was just sent. Wait a minute before asking for another one.");
        if (recent.Count >= CodesPerHour)
            throw new SigningRefusedException("code.too_many", 429, "Too many codes were asked for. Try again in an hour.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.SigningOtps.Add(SigningOtp.Issue(recipient.Id, CodeHash(recipient.Id, code)));
        await db.SaveChangesAsync(ct);

        await email.SendTemplateAsync(recipient.Email, "SigningCode",
            new { recipient.Name, Code = code, DocumentTitle = context.Document.Title, ValidMinutes = (int)SigningOtp.Lifetime.TotalMinutes },
            cancellationToken: ct);
    }

    public async Task<OpenedSigningSession> VerifyCodeAsync(string token, string code, string? ip, CancellationToken ct = default)
    {
        var context = await ResolveAsync(token, null, SigningNeed.Link, ct);
        var recipient = context.Recipient;

        var otp = await db.SigningOtps
            .Where(o => o.RecipientId == recipient.Id && o.UsedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (otp is null || !otp.IsUsable)
            throw new SigningRefusedException("code.expired", 422, "This code is no longer valid. Ask for a new one.");

        var ok = otp.Try(CodeHash(recipient.Id, (code ?? string.Empty).Trim()));
        await db.SaveChangesAsync(ct);
        if (!ok)
        {
            var left = SigningOtp.MaxAttempts - otp.Attempts;
            throw left > 0
                ? new SigningRefusedException("code.invalid", 422, $"Wrong code. {left} attempt(s) left.")
                : new SigningRefusedException("code.expired", 422, "Too many wrong codes. Ask for a new one.");
        }

        return await OpenAsync(recipient, SigningSession.EmailCode, recipient.Email, ip, ct);
    }

    public async Task<OpenedSigningSession> OpenForAccountAsync(Guid recipientId, string? ip, CancellationToken ct = default)
    {
        var recipient = await LoadAsync(r => r.Id == recipientId, ct)
            ?? throw new NotFoundException(nameof(Recipient), recipientId);
        var userEmail = currentUser.UserEmail;
        if (string.IsNullOrWhiteSpace(userEmail))
            throw new ForbiddenAccessException();
        // Signed in as the participant, or as a member of the team or role the step is for (R-M4-3): the account
        // already proves the address.
        var own = string.Equals(recipient.Email, userEmail, StringComparison.OrdinalIgnoreCase);
        var member = !own && currentUser.UserId is { } userId && directory is not null
            && await directory.IsPoolMemberAsync(recipient, userId, ct);
        if (!own && !member)
            throw new ForbiddenAccessException();
        if (recipient.Status == WorkflowStepStatus.Delegated)
            throw new SigningRefusedException("link.delegated", 410, "You handed this step to someone else.");
        return await OpenAsync(recipient, SigningSession.Account, userEmail, ip, ct, currentUser.UserId);
    }

    private async Task<OpenedSigningSession> OpenAsync(Recipient recipient, string method, string verifiedEmail, string? ip,
        CancellationToken ct, Guid? userId = null)
    {
        var secret = SecureTokens.Generate();
        var session = SigningSession.Open(recipient.Id, SecureTokens.Hash(secret), method, verifiedEmail, ip, userId);
        db.SigningSessions.Add(session);
        recipient.MarkVerified(method);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(recipient.DocumentId, AuditAction.AuthenticationEvent, ip ?? "unknown",
            method == SigningSession.Account
                ? $"{recipient.Name} opened the contract signed in to LegalDesk as {verifiedEmail}"
                : $"{recipient.Name} confirmed {verifiedEmail} with an e-mailed code",
            recipientId: recipient.Id, actorName: recipient.Name, actorEmail: verifiedEmail, cancellationToken: ct);

        return new OpenedSigningSession(secret, session.ExpiresAt);
    }

    private Task<Recipient?> LoadAsync(System.Linq.Expressions.Expression<Func<Recipient, bool>> where, CancellationToken ct) =>
        db.Recipients.Include(r => r.Document).FirstOrDefaultAsync(where, ct);

    /// <summary>The code is bound to the participant, so a code hash is worthless for anyone else.</summary>
    public static string CodeHash(Guid recipientId, string code) => SecureTokens.Hash($"{recipientId:N}:{code}");

    /// <summary>"a.ben***@example.com"</summary>
    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***" + (at >= 0 ? email[at..] : string.Empty);
        var keep = Math.Min(3, at - 1);
        return email[..keep] + new string('*', Math.Max(3, at - keep)) + email[at..];
    }
}
