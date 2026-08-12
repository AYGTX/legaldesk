using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IApplicationDbContext
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<SignatureField> SignatureFields => Set<SignatureField>();
    public DbSet<Signature> Signatures => Set<Signature>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<Vacation> Vacations => Set<Vacation>();
    public DbSet<RequestType> RequestTypes => Set<RequestType>();
    public DbSet<ConsentLog> ConsentLogs => Set<ConsentLog>();
    public DbSet<DataSubjectRequest> DataSubjectRequests => Set<DataSubjectRequest>();
    public DbSet<HtmlTemplate> HtmlTemplates => Set<HtmlTemplate>();
    public DbSet<HtmlTemplateField> HtmlTemplateFields => Set<HtmlTemplateField>();
    public DbSet<PdfTemplate> PdfTemplates => Set<PdfTemplate>();
    public DbSet<PdfTemplateField> PdfTemplateFields => Set<PdfTemplateField>();
    public DbSet<Clause> Clauses => Set<Clause>();
    public DbSet<RegulatoryText> RegulatoryTexts => Set<RegulatoryText>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Rename Identity tables
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

        // Global query filters
        builder.Entity<Document>().HasQueryFilter(d => !d.IsDeleted);
        builder.Entity<Template>().HasQueryFilter(t => !t.IsDeleted);
    }
}
