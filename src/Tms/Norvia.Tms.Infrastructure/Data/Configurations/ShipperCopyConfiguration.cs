using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class ShipperCopyConfiguration : IEntityTypeConfiguration<ShipperCopy>
{
    public void Configure(EntityTypeBuilder<ShipperCopy> builder)
    {
        builder.ToTable("ShipperCopies");
        builder.HasKey(copy => copy.Id);
        builder.Property(copy => copy.Id).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(copy => copy.Name).HasMaxLength(200).IsRequired();
    }
}
