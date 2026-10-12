using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class TrackingEventConfiguration : IEntityTypeConfiguration<TrackingEvent>
{
    private const string OwnFleetMarker = "OWN-FLEET";

    public void Configure(EntityTypeBuilder<TrackingEvent> builder)
    {
        builder.ToTable("TrackingEvents");
        builder.Ignore(trackingEvent => trackingEvent.DomainEvents);

        builder.HasKey(trackingEvent => trackingEvent.Id);
        builder.Property(trackingEvent => trackingEvent.Id)
            .HasConversion(id => id.Value, value => new TrackingEventId(value))
            .ValueGeneratedNever();

        // The Shipment's log of Tracking events: many per Shipment, never deleted with it.
        builder.Property(trackingEvent => trackingEvent.ShipmentId)
            .HasConversion(id => id.Value, value => new ShipmentId(value));
        builder.HasOne<Shipment>().WithMany().HasForeignKey(trackingEvent => trackingEvent.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(trackingEvent => trackingEvent.ShipmentId);

        builder.Property(trackingEvent => trackingEvent.ShipmentNumber)
            .HasConversion(number => number.Value, value => ShipmentNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();

        // Who reported it is one column, so a source's event ID can be unique: the Carrier's ID, or a marker for Own fleet,
        // as an index can't take a null that stands for a source.
        builder.Property(trackingEvent => trackingEvent.Source)
            .HasConversion(
                source => source.CarrierId == null ? OwnFleetMarker : source.CarrierId.Value,
                value => value == OwnFleetMarker ? TrackingSource.OwnFleet : TrackingSource.Carrier(new CarrierId(value)))
            .HasColumnName("SourceId")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(trackingEvent => trackingEvent.SourceEventId).HasMaxLength(100).IsRequired();

        // A source's own event ID is unique, so an event sent twice is one event, even when both calls race.
        builder.HasIndex(trackingEvent => new { trackingEvent.Source, trackingEvent.SourceEventId }).IsUnique();
        builder.Property(trackingEvent => trackingEvent.CarrierBookingReference).HasMaxLength(50);

        // An Own fleet event (P12) names its Trip and Driver in place of a Carrier booking; a Trip's Stop outcomes are read from them.
        builder.Property(trackingEvent => trackingEvent.TripNumber)
            .HasConversion(number => number!.Value, value => TripNumber.Parse(value))
            .HasMaxLength(10);
        builder.HasIndex(trackingEvent => trackingEvent.TripNumber);
        builder.Property(trackingEvent => trackingEvent.DriverId)
            .HasConversion(id => id!.Value, value => new DriverId(value))
            .HasMaxLength(50);
        builder.Property(trackingEvent => trackingEvent.TripVersion);

        builder.Property(trackingEvent => trackingEvent.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(trackingEvent => trackingEvent.EventTime).IsRequired();

        builder.ComplexProperty(trackingEvent => trackingEvent.Location, location =>
        {
            location.Property(l => l.Place).HasMaxLength(100).HasColumnName("LocationPlace");
            location.Property(l => l.Postcode).HasMaxLength(20).HasColumnName("LocationPostcode");
            location.Property(l => l.CountryCode).HasMaxLength(2).HasColumnName("LocationCountryCode");
        });

        builder.Property(trackingEvent => trackingEvent.Eta);
        builder.Property(trackingEvent => trackingEvent.Remark).HasMaxLength(500);

        builder.Property(trackingEvent => trackingEvent.CorrelationId)
            .HasConversion(id => id.Value, value => new CorrelationId(value))
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(trackingEvent => trackingEvent.ReceivedAt).IsRequired();
        builder.Property(trackingEvent => trackingEvent.MovedShipment);
    }
}
