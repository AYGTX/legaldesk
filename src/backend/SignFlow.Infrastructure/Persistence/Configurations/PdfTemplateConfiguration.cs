using SignFlow.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class PdfTemplateConfiguration : IEntityTypeConfiguration<PdfTemplate>
{
    public void Configure(EntityTypeBuilder<PdfTemplate> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(300).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(t => t.FileName).HasMaxLength(500).IsRequired();
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
            .WithOne(f => f.PdfTemplate)
            .HasForeignKey(f => f.PdfTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.IsActive);
        builder.HasIndex(t => t.CreatedById);
        builder.HasIndex(t => t.RequestTypeId);

        builder.HasQueryFilter(QueryFilters.SoftDelete, t => !t.IsDeleted);
    }
}

public class PdfTemplateFieldConfiguration : IEntityTypeConfiguration<PdfTemplateField>
{
    public void Configure(EntityTypeBuilder<PdfTemplateField> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.FieldKey).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Label).HasMaxLength(200).IsRequired();
        builder.Property(f => f.AutoFillFrom).HasMaxLength(100);
        builder.Property(f => f.DefaultValue).HasMaxLength(2000);
        builder.Property(f => f.OptionsJson).HasMaxLength(2000);
        builder.Property(f => f.CreatedBy).HasMaxLength(50);
        builder.Property(f => f.UpdatedBy).HasMaxLength(50);

        builder.HasOne(f => f.PreAssignedApprover)
            .WithMany()
            .HasForeignKey(f => f.PreAssignedApproverId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(f => new { f.PdfTemplateId, f.FieldKey }).IsUnique();
        builder.HasIndex(f => f.PreAssignedApproverId);
    }
}
