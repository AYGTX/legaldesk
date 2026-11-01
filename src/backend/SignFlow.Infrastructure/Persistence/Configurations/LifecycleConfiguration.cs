using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class ContractTermConfiguration : IEntityTypeConfiguration<ContractTerm>
{
    public void Configure(EntityTypeBuilder<ContractTerm> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.NoticeProofPath).HasMaxLength(500);
        builder.Property(t => t.CreatedBy).HasMaxLength(50);
        builder.Property(t => t.UpdatedBy).HasMaxLength(50);
        builder.HasOne<Document>().WithOne(d => d.Term).HasForeignKey<ContractTerm>(t => t.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => t.DocumentId).IsUnique();
        builder.HasIndex(t => t.CurrentEndDate);
        builder.Ignore(t => t.IsConfirmed);
        builder.Ignore(t => t.NoticeDeadline);
        builder.Ignore(t => t.Anchor);
    }
}

public class ObligationConfiguration : IEntityTypeConfiguration<Obligation>
{
    public void Configure(EntityTypeBuilder<Obligation> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Title).HasMaxLength(300).IsRequired();
        builder.Property(o => o.Quote).HasMaxLength(4000);
        builder.Property(o => o.CreatedBy).HasMaxLength(50);
        builder.Property(o => o.UpdatedBy).HasMaxLength(50);
        builder.HasOne<Document>().WithMany(d => d.Obligations).HasForeignKey(o => o.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(o => new { o.DocumentId, o.DueDate });
        builder.HasQueryFilter(QueryFilters.SoftDelete, o => !o.IsDeleted);
    }
}

public class ContractEventConfiguration : IEntityTypeConfiguration<ContractEvent>
{
    public void Configure(EntityTypeBuilder<ContractEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Details).HasMaxLength(1000);
        builder.Property(e => e.CreatedBy).HasMaxLength(50);
        builder.Property(e => e.UpdatedBy).HasMaxLength(50);
        builder.HasOne<Document>().WithMany().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.DocumentId, e.At });
    }
}

public class ModelOutputConfiguration : IEntityTypeConfiguration<ModelOutput>
{
    public void Configure(EntityTypeBuilder<ModelOutput> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Feature).HasMaxLength(40).IsRequired();
        builder.Property(o => o.Provider).HasMaxLength(40).IsRequired();
        builder.Property(o => o.Model).HasMaxLength(120).IsRequired();
        builder.Property(o => o.PromptVersion).HasMaxLength(60).IsRequired();
        builder.Property(o => o.Key).HasMaxLength(60);
        builder.Property(o => o.ContentJson).IsRequired();
        builder.Property(o => o.CreatedBy).HasMaxLength(50);
        builder.Property(o => o.UpdatedBy).HasMaxLength(50);
        builder.HasOne<Document>().WithMany().HasForeignKey(o => o.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(o => new { o.DocumentId, o.Feature, o.VersionNumber });
    }
}
