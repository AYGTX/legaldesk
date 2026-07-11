using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class RequestTypeConfiguration : IEntityTypeConfiguration<RequestType>
{
    public void Configure(EntityTypeBuilder<RequestType> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(150);
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.Icon).HasMaxLength(60);
        builder.Property(t => t.Color).HasMaxLength(20);

        builder.HasIndex(t => t.Name).IsUnique();
        builder.HasIndex(t => t.IsActive);
    }
}
