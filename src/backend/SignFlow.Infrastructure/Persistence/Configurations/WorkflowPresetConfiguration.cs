using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class WorkflowPresetConfiguration : IEntityTypeConfiguration<WorkflowPreset>
{
    public void Configure(EntityTypeBuilder<WorkflowPreset> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);

        builder.HasMany(p => p.Steps)
            .WithOne(s => s.WorkflowPreset)
            .HasForeignKey(s => s.WorkflowPresetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.IsActive);
    }
}

public class WorkflowPresetStepConfiguration : IEntityTypeConfiguration<WorkflowPresetStep>
{
    public void Configure(EntityTypeBuilder<WorkflowPresetStep> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Label).HasMaxLength(200);
        builder.Property(s => s.RoleName).HasMaxLength(100);
        builder.Property(s => s.Department).HasMaxLength(200);
        builder.Property(s => s.ConditionFieldKey).HasMaxLength(100);
        builder.Property(s => s.ConditionOperator).HasMaxLength(2);
        builder.Property(s => s.ConditionValue).HasMaxLength(200);

        // A deleted user must not silently turn a named step into an unassigned one: the step keeps its row and
        // resolution then fails with a clear message, so the preset gets fixed.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.ApproverUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.WorkflowPresetId, s.Order });
    }
}
