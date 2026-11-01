using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Interfaces
{
    /// <summary>Which provider and model this server's contract assistant uses.</summary>
    public interface IModelInfo
    {
        string Provider { get; }
        string Model { get; }
        bool IsConfigured { get; }
    }
}

namespace SignFlow.Application.Common.Services
{
    /// <summary>
    /// The prompt behind each assistant feature, by version (R-M12-6). A changed prompt gets a new version; the version is
    /// stored with every output it produced, so an answer can always be traced to the instructions that produced it.
    /// </summary>
    public static class PromptVersions
    {
        public const string Summary = "summary-map/2";
        public const string Chat = "chat/1";
        public const string KeyTerms = "key-terms/1";
        public const string RenewalNote = "renewal-note/1";
        public const string Explainer = "explainer/1";
        public const string TemplateDraft = "template-draft/1";
    }

    /// <summary>How every assistant output is labelled (R-M12-3).</summary>
    public record ModelLabelDto(string Provider, string Model, string PromptVersion, DateTime GeneratedAt);

    /// <summary>
    /// The contract assistant's guard (M12): it exists only when the organisation turned the model on and the server has
    /// one (R-M12-1, otherwise 404 "model.off"); a person is held to 30 calls an hour on a contract and 300 a day
    /// (R-M12-5); and every call that sends contract text is recorded — feature, provider, model, characters, who
    /// (R-M12-2).
    /// </summary>
    public class ModelAccess(IApplicationDbContext db, ITenantContext tenant, IModelInfo info, IAuditService audit)
    {
        public const int PerContractPerHour = 30;
        public const int PerDay = 300;

        public IModelInfo Info => info;

        /// <summary>404 when the assistant is off for this organisation or no model is configured on this server.</summary>
        public async Task RequireOnAsync(CancellationToken ct)
        {
            var mode = await db.Organisations.AsNoTracking()
                .Where(o => o.Id == tenant.OrganisationId).Select(o => (ModelMode?)o.ModelMode).FirstOrDefaultAsync(ct);
            if (mode is null or ModelMode.Off || !info.IsConfigured)
                throw NotFoundException.WithCode("model.off", "The contract assistant is not available for this organisation.");
        }

        /// <summary>The assistant is on, and this person has calls left on this contract.</summary>
        public async Task BeginAsync(Guid documentId, Guid? userId, Guid? recipientId, CancellationToken ct)
        {
            await RequireOnAsync(ct);
            var now = DateTime.UtcNow;
            var mine = db.AuditLogs.AsNoTracking().Where(a => a.Action == AuditAction.ModelUsed
                && (userId != null ? a.UserId == userId : a.RecipientId == recipientId));
            if (await mine.CountAsync(a => a.DocumentId == documentId && a.Timestamp >= now.AddHours(-1), ct) >= PerContractPerHour
                || await mine.CountAsync(a => a.Timestamp >= now.AddDays(-1), ct) >= PerDay)
                throw new TooManyRequestsException("model.limit", "You have reached the number of assistant requests allowed for now. Try again later.");
        }

        public Task RecordAsync(Guid documentId, string feature, int charactersSent, Guid? userId, string? actorEmail,
            Guid? recipientId = null, string? actorName = null, CancellationToken ct = default) =>
            audit.LogAsync(documentId, AuditAction.ModelUsed, "system",
                $"Contract assistant: {feature}; {info.Provider} {info.Model}; {charactersSent} characters sent",
                userId, recipientId, actorName: actorName, actorEmail: actorEmail,
                metadata: $"{{\"feature\":\"{feature}\",\"provider\":\"{info.Provider}\",\"model\":\"{info.Model}\",\"chars\":{charactersSent}}}",
                cancellationToken: ct);

        public ModelLabelDto Label(string promptVersion, DateTime? at = null) => new(info.Provider, info.Model, promptVersion, at ?? DateTime.UtcNow);
    }
}
