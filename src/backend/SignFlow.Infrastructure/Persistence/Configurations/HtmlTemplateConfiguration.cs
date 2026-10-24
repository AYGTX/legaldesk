using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class HtmlTemplateConfiguration : IEntityTypeConfiguration<HtmlTemplate>
{
    public void Configure(EntityTypeBuilder<HtmlTemplate> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(300).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.HtmlContent).IsRequired();
        builder.Property(t => t.UpdatedBy).HasMaxLength(50);

        builder.HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.DefaultApprover)
            .WithMany()
            .HasForeignKey(t => t.DefaultApproverId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.RequestType)
            .WithMany()
            .HasForeignKey(t => t.RequestTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.WorkflowPreset)
            .WithMany()
            .HasForeignKey(t => t.WorkflowPresetId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(t => t.Fields)
            .WithOne(f => f.HtmlTemplate)
            .HasForeignKey(f => f.HtmlTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.IsActive);
        builder.HasIndex(t => t.CreatedById);
        builder.HasIndex(t => t.RequestTypeId);

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class HtmlTemplateFieldConfiguration : IEntityTypeConfiguration<HtmlTemplateField>
{
    public void Configure(EntityTypeBuilder<HtmlTemplateField> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.FieldKey).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Label).HasMaxLength(200).IsRequired();
        builder.Property(f => f.AutoFillFrom).HasMaxLength(100);
        builder.Property(f => f.OptionsJson).HasMaxLength(2000);
        builder.Property(f => f.DefaultValue).HasMaxLength(1000);
        builder.Property(f => f.CreatedBy).HasMaxLength(50);
        builder.Property(f => f.UpdatedBy).HasMaxLength(50);

        builder.HasOne(f => f.PreAssignedApprover)
            .WithMany()
            .HasForeignKey(f => f.PreAssignedApproverId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(f => new { f.HtmlTemplateId, f.FieldKey }).IsUnique();
    }
}
