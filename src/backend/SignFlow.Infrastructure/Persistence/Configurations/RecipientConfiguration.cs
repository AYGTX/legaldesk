using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class RecipientConfiguration : IEntityTypeConfiguration<Recipient>
{
    public void Configure(EntityTypeBuilder<Recipient> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Email).HasMaxLength(256).IsRequired();
        builder.Property(r => r.SecureToken).HasMaxLength(100).IsRequired();
        builder.Property(r => r.SignedIp).HasMaxLength(50);
        builder.Property(r => r.SignedUserAgent).HasMaxLength(500);
        builder.Property(r => r.SignedLocation).HasMaxLength(200);
        builder.Property(r => r.DeclineReason).HasMaxLength(2000);
        builder.Property(r => r.DelegationNote).HasMaxLength(1000);
        builder.Property(r => r.PrivateMessage).HasMaxLength(2000);
        builder.Property(r => r.Company).HasMaxLength(200);
        builder.Property(r => r.RoleLabel).HasMaxLength(100);

        builder.HasMany(r => r.Signatures)
            .WithOne(s => s.Recipient)
            .HasForeignKey(s => s.RecipientId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(r => r.SecureToken).IsUnique();
        builder.HasIndex(r => r.DocumentId);
        builder.HasIndex(r => r.Email);
        builder.HasIndex(r => new { r.DocumentId, r.Order });
    }
}
