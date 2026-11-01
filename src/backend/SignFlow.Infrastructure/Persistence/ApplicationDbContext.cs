using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext? tenant = null)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IApplicationDbContext
{
    /// <summary>The organisation this context acts for. Without a registered one (some tests, design-time tools)
    /// the context gets its own, unscoped until someone calls BeginScope.</summary>
    public ITenantContext Tenant { get; } = tenant ?? new TenantContext();

    // Read by the tenant filter on every query, so a scope opened after the context was built still applies.
    private Guid? CurrentOrganisationId => Tenant.OrganisationId;

    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();
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
    public DbSet<ContractTerm> ContractTerms => Set<ContractTerm>();
    public DbSet<Obligation> Obligations => Set<Obligation>();
    public DbSet<ContractEvent> ContractEvents => Set<ContractEvent>();
    public DbSet<ClauseVersion> ClauseVersions => Set<ClauseVersion>();
    public DbSet<ClauseRuleLink> ClauseRuleLinks => Set<ClauseRuleLink>();
    public DbSet<DocumentReadinessItem> DocumentReadinessItems => Set<DocumentReadinessItem>();
    public DbSet<ReviewRun> ReviewRuns => Set<ReviewRun>();
    public DbSet<SigningOtp> SigningOtps => Set<SigningOtp>();
    public DbSet<SigningSession> SigningSessions => Set<SigningSession>();
    public DbSet<OrganisationRuleSetting> OrganisationRuleSettings => Set<OrganisationRuleSetting>();
    public DbSet<WorkflowPreset> WorkflowPresets => Set<WorkflowPreset>();
    public DbSet<WorkflowPresetStep> WorkflowPresetSteps => Set<WorkflowPresetStep>();
    public DbSet<ContractRound> ContractRounds => Set<ContractRound>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<OrganisationHoliday> OrganisationHolidays => Set<OrganisationHoliday>();

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

        builder.Entity<Organisation>(o =>
        {
            o.HasIndex(x => x.Slug).IsUnique();
            o.Property(x => x.ModelMode).HasConversion<string>().HasMaxLength(20);
        });

        // Sessions and invitations are found by the hash of the token someone presents.
        builder.Entity<UserSession>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.PreviousTokenHash);
            e.HasIndex(x => x.UserId);
        });
        builder.Entity<UserInvitation>().HasIndex(x => x.TokenHash).IsUnique();
        builder.Entity<ApplicationUser>().Property(u => u.PreferredLanguage).HasMaxLength(5);

        // The dispatcher reads due messages in order.
        builder.Entity<OutboxMessage>().HasIndex(m => new { m.SentAt, m.NextAttemptAt });

        // Soft delete
        builder.Entity<Document>().HasQueryFilter(QueryFilters.SoftDelete, d => !d.IsDeleted);
        builder.Entity<Template>().HasQueryFilter(QueryFilters.SoftDelete, t => !t.IsDeleted);

        // Tenancy (R-M0-2, R-M0-3): every owned table points at its organisation and is filtered by it.
        var configure = typeof(ApplicationDbContext).GetMethod(nameof(ConfigureTenantOwned), BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var type in builder.Model.GetEntityTypes().Select(e => e.ClrType)
                     .Where(t => typeof(ITenantOwned).IsAssignableFrom(t) && !t.IsAbstract).ToList())
            configure.MakeGenericMethod(type).Invoke(this, [builder]);
    }

    private void ConfigureTenantOwned<T>(ModelBuilder builder) where T : class, ITenantOwned
    {
        var entity = builder.Entity<T>();
        entity.HasOne<Organisation>().WithMany().HasForeignKey(e => e.OrganisationId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(e => e.OrganisationId);
        entity.HasQueryFilter(QueryFilters.Tenant, e => CurrentOrganisationId == null || e.OrganisationId == CurrentOrganisationId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampOrganisation();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampOrganisation();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// New rows take the current organisation; a row of another organisation cannot be written while acting for
    /// this one (R-M0-3). Without a scope, only rows that already carry an organisation may be written.
    /// </summary>
    private void StampOrganisation()
    {
        var current = Tenant.OrganisationId;
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (entry.State == EntityState.Added && entry.Entity.OrganisationId == Guid.Empty)
            {
                if (current is null)
                    throw new InvalidOperationException(
                        $"A new {entry.Entity.GetType().Name} has no organisation and no organisation is in scope.");
                entry.Entity.AssignOrganisation(current.Value);
            }
            else if (current is not null && entry.Entity.OrganisationId != current)
            {
                throw new InvalidOperationException(
                    $"A {entry.Entity.GetType().Name} of another organisation cannot be written from this one.");
            }
        }
    }
}
