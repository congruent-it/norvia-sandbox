using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class VehicleCopyConfiguration : IEntityTypeConfiguration<VehicleCopy>
{
    public void Configure(EntityTypeBuilder<VehicleCopy> builder)
    {
        builder.ToTable("VehicleCopies");
        builder.HasKey(copy => copy.Id);
        builder.Property(copy => copy.Id).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(copy => copy.Registration).HasMaxLength(50).IsRequired();
        builder.Property(copy => copy.Type).HasMaxLength(30).IsRequired();
    }
}
