using Microsoft.EntityFrameworkCore;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Organisation> Organisations { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<UserSession> UserSessions { get; }
    DbSet<UserInvitation> UserInvitations { get; }
    DbSet<Document> Documents { get; }
    DbSet<DocumentVersion> DocumentVersions { get; }
    DbSet<Recipient> Recipients { get; }
    DbSet<ChecklistItem> ChecklistItems { get; }
    DbSet<SignatureField> SignatureFields { get; }
    DbSet<Signature> Signatures { get; }
    DbSet<Workflow> Workflows { get; }
    DbSet<WorkflowStep> WorkflowSteps { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Comment> Comments { get; }
    DbSet<Template> Templates { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<ApplicationUser> Users { get; }
    DbSet<Vacation> Vacations { get; }
    DbSet<RequestType> RequestTypes { get; }
    DbSet<ConsentLog> ConsentLogs { get; }
    DbSet<DataSubjectRequest> DataSubjectRequests { get; }
    DbSet<HtmlTemplate> HtmlTemplates { get; }
    DbSet<HtmlTemplateField> HtmlTemplateFields { get; }
    DbSet<PdfTemplate> PdfTemplates { get; }
    DbSet<PdfTemplateField> PdfTemplateFields { get; }
    DbSet<Clause> Clauses { get; }
    DbSet<RegulatoryText> RegulatoryTexts { get; }
    DbSet<RegulatoryArticle> RegulatoryArticles { get; }
    DbSet<DocumentReadinessItem> DocumentReadinessItems { get; }
    DbSet<ReviewRun> ReviewRuns { get; }
    DbSet<SigningOtp> SigningOtps { get; }
    DbSet<SigningSession> SigningSessions { get; }
    DbSet<OrganisationRuleSetting> OrganisationRuleSettings { get; }
    DbSet<WorkflowPreset> WorkflowPresets { get; }
    DbSet<WorkflowPresetStep> WorkflowPresetSteps { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
