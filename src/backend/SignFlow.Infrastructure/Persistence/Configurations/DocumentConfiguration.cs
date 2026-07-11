using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Title).HasMaxLength(500).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(2000);
        builder.Property(d => d.FileName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.FinalStoragePath).HasMaxLength(1000);
        builder.Property(d => d.ContentType).HasMaxLength(100);
        builder.Property(d => d.Hash).HasMaxLength(256);
        builder.Property(d => d.Tags).HasMaxLength(500);
        builder.Property(d => d.VerificationCode).HasMaxLength(20);
        builder.Property(d => d.Message).HasMaxLength(5000);
        builder.Property(d => d.Subject).HasMaxLength(500);
        builder.Property(d => d.WatermarkText).HasMaxLength(200);
        builder.Property(d => d.CreatedBy).HasMaxLength(50);
        builder.Property(d => d.UpdatedBy).HasMaxLength(50);
        builder.Property(d => d.RowVersion).IsRowVersion();

        builder.HasOne(d => d.Owner)
            .WithMany(u => u.Documents)
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.RequestType)
            .WithMany()
            .HasForeignKey(d => d.RequestTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.Workflow)
            .WithOne(w => w.Document)
            .HasForeignKey<Workflow>(w => w.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Recipients)
            .WithOne(r => r.Document)
            .HasForeignKey(r => r.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.SignatureFields)
            .WithOne(f => f.Document)
            .HasForeignKey(f => f.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.AuditLogs)
            .WithOne(a => a.Document)
            .HasForeignKey(a => a.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Comments)
            .WithOne(c => c.Document)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Versions)
            .WithOne(v => v.Document)
            .HasForeignKey(v => v.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.HtmlTemplate)
            .WithMany()
            .HasForeignKey(d => d.HtmlTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => d.OwnerId);
        builder.HasIndex(d => d.Status);
        builder.HasIndex(d => d.VerificationCode).IsUnique();
        builder.HasIndex(d => d.CreatedAt);
        builder.HasIndex(d => d.ExpiresAt);
        builder.HasIndex(d => d.HtmlTemplateId);
    }
}
