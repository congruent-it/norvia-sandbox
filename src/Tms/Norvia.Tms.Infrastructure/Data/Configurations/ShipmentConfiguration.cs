using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.CarrierBookings;
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

        // The history of assignments is read and written only with its Shipment, so it is one JSON column. A Shipment always
        // has its planning's assignment, so the column is never empty.
        builder.Property(shipment => shipment.Assignments)
            .HasJsonList(StoredAssignment.Of, stored => stored.ToAssignment())
            .IsRequired();

        // The delivery windows the Planner agreed are read and written only with their Shipment, like its assignments, so they are
        // one JSON column too. It is empty until the Planner agrees one.
        builder.Property(shipment => shipment.AgreedDeliveryWindows)
            .HasJsonList(StoredAgreedDeliveryWindow.Of, stored => stored.ToWindow())
            .HasDefaultValueSql("N'[]'")
            .IsRequired();

        // A flag, not a status (P12): set when the Driver ends a Trip without delivering a collected Shipment.
        builder.Property(shipment => shipment.AwaitingRedelivery).HasDefaultValue(false);

        builder.Property(shipment => shipment.CorrelationId)
            .HasConversion(id => id.Value, value => new CorrelationId(value))
            .HasMaxLength(128)
            .IsRequired();

        // The release that handed the Shipment over, in columns of its own; all null while the Shipment is Planned.
        builder.ComplexProperty(shipment => shipment.LastRelease, release =>
        {
            release.Property(r => r.CorrelationId)
                .HasConversion(id => id.Value, value => new CorrelationId(value))
                .HasMaxLength(128)
                .HasColumnName("ReleaseCorrelationId");
            release.Property(r => r.ReleasedAt).HasColumnName("ReleasedAt");
            release.Property(r => r.Key)
                .HasConversion(key => key.Value, value => new IdempotencyKey(value))
                .HasMaxLength(IdempotencyKey.MaxLength)
                .HasColumnName("ReleaseKey");
            release.Property(r => r.CarrierBookingNumber)
                .HasConversion(number => number!.Value, value => CarrierBookingNumber.Parse(value))
                .HasMaxLength(10)
                .HasColumnName("CarrierBookingNumber");
        });

        // Planning changes a Shipment after it is made (reassign, release), so two Planners' changes must not both land.
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}

/// <summary>A <see cref="ShipmentAssignment"/> as the JSON column stores it, in plain values: a null Carrier is Own fleet.</summary>
internal sealed record StoredAssignment(string? CarrierId, string CorrelationId, DateTimeOffset AssignedAt, string? Key)
{
    public static StoredAssignment Of(ShipmentAssignment assignment) =>
        new(assignment.CarriedBy.CarrierId?.Value, assignment.CorrelationId.Value, assignment.AssignedAt, assignment.Key?.Value);

    public ShipmentAssignment ToAssignment() => new(
        CarrierId is null ? CarriedBy.OwnFleet : CarriedBy.Carrier(new CarrierId(CarrierId)),
        new CorrelationId(CorrelationId),
        AssignedAt,
        Key is null ? null : new IdempotencyKey(Key));
}

/// <summary>An <see cref="AgreedDeliveryWindow"/> as the JSON column stores it, in plain values.</summary>
internal sealed record StoredAgreedDeliveryWindow(
    DateTimeOffset Earliest, DateTimeOffset Latest, string ExceptionNumber, string CorrelationId, DateTimeOffset AgreedAt, string Key)
{
    public static StoredAgreedDeliveryWindow Of(AgreedDeliveryWindow agreed) =>
        new(agreed.Earliest, agreed.Latest, agreed.ExceptionNumber.Value, agreed.CorrelationId.Value, agreed.AgreedAt, agreed.Key.Value);

    public AgreedDeliveryWindow ToWindow() => new(Earliest, Latest, Norvia.Tms.Domain.Exceptions.ExceptionNumber.Parse(ExceptionNumber), new CorrelationId(CorrelationId), AgreedAt, new IdempotencyKey(Key));
}
