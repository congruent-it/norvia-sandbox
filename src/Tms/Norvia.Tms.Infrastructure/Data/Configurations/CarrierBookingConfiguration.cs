using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class CarrierBookingConfiguration : IEntityTypeConfiguration<CarrierBooking>
{
    public void Configure(EntityTypeBuilder<CarrierBooking> builder)
    {
        builder.ToTable("CarrierBookings");
        builder.Ignore(booking => booking.DomainEvents);

        builder.HasKey(booking => booking.Id);
        builder.Property(booking => booking.Id)
            .HasConversion(id => id.Value, value => new CarrierBookingId(value))
            .ValueGeneratedNever();

        builder.Property(booking => booking.Number)
            .HasConversion(number => number.Value, value => CarrierBookingNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();
        builder.HasIndex(booking => booking.Number).IsUnique();

        // A Shipment can have several Carrier bookings over its life (P4), so this is not unique.
        builder.Property(booking => booking.ShipmentId)
            .HasConversion(id => id.Value, value => new ShipmentId(value));
        builder.HasOne<Shipment>().WithMany().HasForeignKey(booking => booking.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(booking => booking.ShipmentId);

        builder.Property(booking => booking.ShipmentNumber)
            .HasConversion(number => number.Value, value => ShipmentNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(booking => booking.CarrierId)
            .HasConversion(id => id.Value, value => new CarrierId(value))
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(booking => booking.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(booking => booking.CorrelationId)
            .HasConversion(id => id.Value, value => new CorrelationId(value))
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(booking => booking.RequestedAt).IsRequired();
    }
}
