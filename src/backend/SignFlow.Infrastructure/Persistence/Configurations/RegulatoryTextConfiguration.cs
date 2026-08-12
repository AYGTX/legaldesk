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

        builder.HasIndex(r => r.Category);
        builder.HasIndex(r => r.Jurisdiction);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.Language);

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
