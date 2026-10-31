using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence.Configurations;

public class VacationConfiguration : IEntityTypeConfiguration<Vacation>
{
    public void Configure(EntityTypeBuilder<Vacation> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Note).HasMaxLength(500);
        builder.Property(v => v.StartDate).IsRequired();
        builder.Property(v => v.EndDate).IsRequired();

        builder.HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(v => v.DelegateUserId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(v => v.UserId);
        builder.HasIndex(v => new { v.StartDate, v.EndDate });
    }
}
