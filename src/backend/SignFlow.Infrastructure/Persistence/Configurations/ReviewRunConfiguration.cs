using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class ReviewRunConfiguration : IEntityTypeConfiguration<ReviewRun>
{
    public void Configure(EntityTypeBuilder<ReviewRun> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.GridVersion).HasMaxLength(64);
        builder.Property(r => r.ErrorCode).HasMaxLength(64);
        builder.Property(r => r.Error).HasMaxLength(1000);
        builder.Property(r => r.CreatedBy).HasMaxLength(50);
        builder.Property(r => r.UpdatedBy).HasMaxLength(50);

        builder.HasOne<Document>().WithMany().HasForeignKey(r => r.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.DocumentId, r.CreatedAt });
    }
}
