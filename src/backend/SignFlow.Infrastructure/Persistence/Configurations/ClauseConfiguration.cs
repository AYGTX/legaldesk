using SignFlow.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class ClauseConfiguration : IEntityTypeConfiguration<Clause>
{
    public void Configure(EntityTypeBuilder<Clause> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Guidance).HasMaxLength(2000);
        builder.Property(c => c.CreatedBy).HasMaxLength(50);
        builder.Property(c => c.UpdatedBy).HasMaxLength(50);

        builder.HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => c.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.RequestType)
            .WithMany()
            .HasForeignKey(c => c.RequestTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(c => c.Versions).WithOne().HasForeignKey(v => v.ClauseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.RuleLinks).WithOne().HasForeignKey(l => l.ClauseId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.Category);
        builder.HasIndex(c => c.RequestTypeId);

        builder.HasQueryFilter(QueryFilters.SoftDelete, c => !c.IsDeleted);
    }
}

public class ClauseVersionConfiguration : IEntityTypeConfiguration<ClauseVersion>
{
    public void Configure(EntityTypeBuilder<ClauseVersion> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Body).IsRequired();
        builder.Property(v => v.Language).HasMaxLength(5).IsRequired();
        builder.Property(v => v.CreatedBy).HasMaxLength(50);
        builder.Property(v => v.UpdatedBy).HasMaxLength(50);
        builder.HasIndex(v => new { v.ClauseId, v.Number }).IsUnique();
        builder.HasIndex(v => v.Status);
    }
}

public class ClauseRuleLinkConfiguration : IEntityTypeConfiguration<ClauseRuleLink>
{
    public void Configure(EntityTypeBuilder<ClauseRuleLink> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.RuleKey).HasMaxLength(120).IsRequired();
        builder.Property(l => l.CreatedBy).HasMaxLength(50);
        builder.Property(l => l.UpdatedBy).HasMaxLength(50);
        builder.HasIndex(l => new { l.ClauseId, l.RuleKey }).IsUnique();
        builder.HasIndex(l => l.RuleKey);
    }
}
