using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class DocumentReadinessItemConfiguration : IEntityTypeConfiguration<DocumentReadinessItem>
{
    public void Configure(EntityTypeBuilder<DocumentReadinessItem> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Severity).HasMaxLength(32).IsRequired();
        builder.Property(i => i.SourceKey).HasMaxLength(256).IsRequired();
        builder.Property(i => i.RuleKey).HasMaxLength(80);
        builder.Property(i => i.RuleApprovedBy).HasMaxLength(300);
        builder.Property(i => i.ClauseReference).HasMaxLength(300);
        builder.Property(i => i.DetectionKind).HasMaxLength(32);
        builder.Property(i => i.Title).HasMaxLength(300).IsRequired();
        builder.Property(i => i.Detail).IsRequired();
        builder.Property(i => i.Reason).HasMaxLength(DocumentReadinessItem.MaxReasonLength);
        builder.Property(i => i.CreatedBy).HasMaxLength(50);
        builder.Property(i => i.UpdatedBy).HasMaxLength(50);

        // The reconciliation of a review looks items up by (document, source key); the current findings by run.
        builder.HasIndex(i => new { i.DocumentId, i.SourceKey });
        builder.HasIndex(i => i.LastSeenRunId);
        builder.HasIndex(i => i.Status);

        // The relationship to Document is declared in DocumentConfiguration, with the rest of the
        // document's children.
    }
}
