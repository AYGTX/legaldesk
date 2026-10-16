using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class RegulatoryTextConfiguration : IEntityTypeConfiguration<RegulatoryText>
{
    public void Configure(EntityTypeBuilder<RegulatoryText> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Title).HasMaxLength(300).IsRequired();
        builder.Property(r => r.Reference).HasMaxLength(300);
        builder.Property(r => r.SourceUrl).HasMaxLength(500);
        builder.Property(r => r.Summary).IsRequired();
        builder.Property(r => r.Keywords).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.Language).HasMaxLength(5).IsRequired();
        builder.Property(r => r.CreatedBy).HasMaxLength(50);
        builder.Property(r => r.UpdatedBy).HasMaxLength(50);

        builder.HasOne(r => r.CreatedByUser)
            .WithMany()
            .HasForeignKey(r => r.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.RequestType)
            .WithMany()
            .HasForeignKey(r => r.RequestTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(r => r.Articles)
            .WithOne(a => a.RegulatoryText)
            .HasForeignKey(a => a.RegulatoryTextId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.Category);
        builder.HasIndex(r => r.Jurisdiction);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.Language);

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class RegulatoryArticleConfiguration : IEntityTypeConfiguration<RegulatoryArticle>
{
    public void Configure(EntityTypeBuilder<RegulatoryArticle> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Number).HasMaxLength(30).IsRequired();
        builder.Property(a => a.Text).IsRequired();
        builder.Property(a => a.CreatedBy).HasMaxLength(50);
        builder.Property(a => a.UpdatedBy).HasMaxLength(50);

        builder.HasIndex(a => new { a.RegulatoryTextId, a.Order });
        builder.HasIndex(a => new { a.RegulatoryTextId, a.Number });
    }
}
