using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class OrganisationRuleSettingConfiguration : IEntityTypeConfiguration<OrganisationRuleSetting>
{
    public void Configure(EntityTypeBuilder<OrganisationRuleSetting> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.RuleKey).HasMaxLength(80).IsRequired();
        builder.Property(s => s.SeverityOverride).HasMaxLength(32);
        builder.Property(s => s.CreatedBy).HasMaxLength(50);
        builder.Property(s => s.UpdatedBy).HasMaxLength(50);

        // One setting per rule per organisation.
        builder.HasIndex(s => new { s.OrganisationId, s.RuleKey }).IsUnique();
    }
}
