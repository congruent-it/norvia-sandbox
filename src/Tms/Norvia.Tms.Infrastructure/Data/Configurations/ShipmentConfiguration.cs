using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments");
        builder.Ignore(shipment => shipment.DomainEvents);

        builder.HasKey(shipment => shipment.Id);
        builder.Property(shipment => shipment.Id)
            .HasConversion(id => id.Value, value => new ShipmentId(value))
            .ValueGeneratedNever();

        builder.Property(shipment => shipment.Number)
            .HasConversion(number => number.Value, value => ShipmentNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();
        builder.HasIndex(shipment => shipment.Number).IsUnique();

        // One Shipment per Transport Order (ADR 0007): even two assignments of one Transport Order at the same moment
        // never give it two Shipments.
        builder.Property(shipment => shipment.TransportOrderId)
            .HasConversion(id => id.Value, value => new TransportOrderId(value));
        builder.HasOne<TransportOrder>().WithOne().HasForeignKey<Shipment>(shipment => shipment.TransportOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(shipment => shipment.TransportOrderId).IsUnique();

        builder.Property(shipment => shipment.TransportOrderNumber)
            .HasConversion(number => number.Value, value => TransportOrderNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(shipment => shipment.ShipperId)
            .HasConversion(id => id.Value, value => new ShipperId(value))
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(shipment => shipment.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Who carries the Shipment is one column: the Carrier's ID, or null for Own fleet.
        builder.ComplexProperty(shipment => shipment.CarriedBy, carriedBy =>
            carriedBy.Property(c => c.CarrierId)
                .HasConversion(id => id!.Value, value => new CarrierId(value))
                .HasMaxLength(50)
                .HasColumnName("CarrierId"));

        builder.Property(shipment => shipment.CorrelationId)
            .HasConversion(id => id.Value, value => new CorrelationId(value))
            .HasMaxLength(128)
            .IsRequired();

        // Planning changes a Shipment after it is made (reassign, release), so two Planners' changes must not both land.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
