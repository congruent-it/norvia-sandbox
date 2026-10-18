using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Infrastructure.Data.Configurations;

internal sealed class ShipperConfiguration : IEntityTypeConfiguration<Shipper>
{
    public void Configure(EntityTypeBuilder<Shipper> builder)
    {
        builder.ToTable("Shippers");
        builder.Ignore(shipper => shipper.DomainEvents);
        builder.HasKey(shipper => shipper.Id);
        builder.Property(shipper => shipper.Id).HasConversion(id => id.Value, value => new ShipperId(value)).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(shipper => shipper.Name).HasMaxLength(200).IsRequired();
    }
}
