using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.IpAddress).HasMaxLength(50).IsRequired();
        builder.Property(a => a.UserAgent).HasMaxLength(500);
        builder.Property(a => a.Location).HasMaxLength(200);
        builder.Property(a => a.Description).HasMaxLength(2000);
        builder.Property(a => a.ActorName).HasMaxLength(200);
        builder.Property(a => a.ActorEmail).HasMaxLength(256);
        builder.Property(a => a.Metadata).HasMaxLength(5000);

        builder.HasOne(a => a.User)
            .WithMany(u => u.AuditLogs)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => a.DocumentId);
        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => a.Action);
    }
}
