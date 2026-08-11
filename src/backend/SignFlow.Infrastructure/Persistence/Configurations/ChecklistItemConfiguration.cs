using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Text).HasMaxLength(500).IsRequired();

        builder.HasOne(c => c.Recipient)
            .WithMany(r => r.ChecklistItems)
            .HasForeignKey(c => c.RecipientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.RecipientId, c.Order });
    }
}
