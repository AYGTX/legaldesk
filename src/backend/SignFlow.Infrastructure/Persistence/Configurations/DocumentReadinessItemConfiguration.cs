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
        builder.Property(i => i.Title).HasMaxLength(300).IsRequired();
        builder.Property(i => i.Detail).IsRequired();
        builder.Property(i => i.ResolutionNote).HasMaxLength(1000);
        builder.Property(i => i.CreatedBy).HasMaxLength(50);
        builder.Property(i => i.UpdatedBy).HasMaxLength(50);

        // The reconciliation on re-run looks items up by (document, source key).
        builder.HasIndex(i => new { i.DocumentId, i.SourceKey });
        builder.HasIndex(i => i.Status);

        // The relationship to Document is declared in DocumentConfiguration, with the rest of the
        // document's children.
    }
}
