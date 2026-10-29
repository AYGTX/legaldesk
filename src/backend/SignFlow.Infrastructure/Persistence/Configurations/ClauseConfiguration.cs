using SignFlow.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class ClauseConfiguration : IEntityTypeConfiguration<Clause>
{
    public void Configure(EntityTypeBuilder<Clause> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Body).IsRequired();
        builder.Property(c => c.Guidance).HasMaxLength(2000);
        builder.Property(c => c.Language).HasMaxLength(5).IsRequired();
        builder.Property(c => c.RiskLevel).HasMaxLength(20);
        builder.Property(c => c.CreatedBy).HasMaxLength(50);
        builder.Property(c => c.UpdatedBy).HasMaxLength(50);

        builder.HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => c.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.RequestType)
            .WithMany()
            .HasForeignKey(c => c.RequestTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.Category);
        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.Language);
        builder.HasIndex(c => c.RequestTypeId);

        builder.HasQueryFilter(QueryFilters.SoftDelete, c => !c.IsDeleted);
    }
}
