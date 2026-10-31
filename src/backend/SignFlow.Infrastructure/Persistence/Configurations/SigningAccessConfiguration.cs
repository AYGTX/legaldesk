using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class SigningOtpConfiguration : IEntityTypeConfiguration<SigningOtp>
{
    public void Configure(EntityTypeBuilder<SigningOtp> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(o => o.CreatedBy).HasMaxLength(50);
        builder.Property(o => o.UpdatedBy).HasMaxLength(50);
        builder.HasOne<Recipient>().WithMany().HasForeignKey(o => o.RecipientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(o => new { o.RecipientId, o.CreatedAt });
    }
}

public class SigningSessionConfiguration : IEntityTypeConfiguration<SigningSession>
{
    public void Configure(EntityTypeBuilder<SigningSession> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SecretHash).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Method).HasMaxLength(32).IsRequired();
        builder.Property(s => s.VerifiedEmail).HasMaxLength(256).IsRequired();
        builder.Property(s => s.Ip).HasMaxLength(64);
        builder.Property(s => s.CreatedBy).HasMaxLength(50);
        builder.Property(s => s.UpdatedBy).HasMaxLength(50);
        builder.HasOne<Recipient>().WithMany().HasForeignKey(s => s.RecipientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.SecretHash).IsUnique();
    }
}
