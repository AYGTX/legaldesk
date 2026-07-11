using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.CompletionMessage).HasMaxLength(2000);

        builder.HasMany(w => w.Steps)
            .WithOne(s => s.Workflow)
            .HasForeignKey(s => s.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(w => w.DocumentId).IsUnique();
        builder.HasIndex(w => w.Status);
    }
}

public class WorkflowStepConfiguration : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ConditionalExpression).HasMaxLength(2000);
        builder.Property(s => s.EscalationEmail).HasMaxLength(256);
        builder.Property(s => s.Instructions).HasMaxLength(2000);
        builder.Property(s => s.DeclineReason).HasMaxLength(2000);

        builder.HasOne(s => s.Recipient)
            .WithMany()
            .HasForeignKey(s => s.RecipientId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(s => s.WorkflowId);
        builder.HasIndex(s => new { s.WorkflowId, s.Order });
    }
}
