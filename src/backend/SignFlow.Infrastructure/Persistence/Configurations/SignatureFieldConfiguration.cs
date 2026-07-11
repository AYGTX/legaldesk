using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class SignatureFieldConfiguration : IEntityTypeConfiguration<SignatureField>
{
    public void Configure(EntityTypeBuilder<SignatureField> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Label).HasMaxLength(200);
        builder.Property(f => f.Placeholder).HasMaxLength(500);
        builder.Property(f => f.DefaultValue).HasMaxLength(1000);
        builder.Property(f => f.FontFamily).HasMaxLength(100);
        builder.Property(f => f.FontColor).HasMaxLength(20);
        builder.Property(f => f.BorderColor).HasMaxLength(20);
        builder.Property(f => f.BackgroundColor).HasMaxLength(20);
        builder.Property(f => f.ValidationRule).HasMaxLength(500);
        builder.Property(f => f.Options).HasMaxLength(5000);
        builder.Property(f => f.VisibilityRules).HasMaxLength(2000);
        builder.Property(f => f.GroupName).HasMaxLength(100);
        builder.Property(f => f.SignedValue).HasMaxLength(5000);
        builder.Property(f => f.FieldName).HasMaxLength(100);
        builder.Property(f => f.Tooltip).HasMaxLength(500);

        builder.HasOne(f => f.Recipient)
            .WithMany(r => r.AssignedFields)
            .HasForeignKey(f => f.RecipientId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(f => f.Signature)
            .WithOne(s => s.SignatureField)
            .HasForeignKey<Signature>(s => s.SignatureFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.DocumentId);
        builder.HasIndex(f => f.RecipientId);
        builder.HasIndex(f => new { f.DocumentId, f.Page });
    }
}
