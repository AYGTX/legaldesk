using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class ContractRoundConfiguration : IEntityTypeConfiguration<ContractRound>
{
    public void Configure(EntityTypeBuilder<ContractRound> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.CreatedBy).HasMaxLength(50);
        builder.Property(r => r.UpdatedBy).HasMaxLength(50);
        builder.HasOne<Document>().WithMany().HasForeignKey(r => r.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.DocumentId, r.Number }).IsUnique();
    }
}

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(120).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.CreatedBy).HasMaxLength(50);
        builder.Property(t => t.UpdatedBy).HasMaxLength(50);
        builder.HasMany(t => t.Members).WithOne(m => m.Team).HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.OrganisationId, t.Name }).IsUnique();
    }
}

public class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.CreatedBy).HasMaxLength(50);
        builder.Property(m => m.UpdatedBy).HasMaxLength(50);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.TeamId, m.UserId }).IsUnique();
    }
}

public class OrganisationHolidayConfiguration : IEntityTypeConfiguration<OrganisationHoliday>
{
    public void Configure(EntityTypeBuilder<OrganisationHoliday> builder)
    {
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Name).HasMaxLength(120).IsRequired();
        builder.Property(h => h.CreatedBy).HasMaxLength(50);
        builder.Property(h => h.UpdatedBy).HasMaxLength(50);
        builder.HasIndex(h => new { h.OrganisationId, h.Date });
    }
}
