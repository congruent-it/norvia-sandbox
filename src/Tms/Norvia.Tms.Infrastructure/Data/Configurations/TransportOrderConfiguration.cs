using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class TransportOrderConfiguration : IEntityTypeConfiguration<TransportOrder>
{
    public const string ShipperReferenceIndex = "IX_TransportOrders_ShipperId_ShipperReference";

    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions EnumNamesJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<TransportOrder> builder)
    {
        builder.ToTable("TransportOrders");
        builder.Ignore(order => order.DomainEvents);

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id)
            .HasConversion(id => id.Value, value => new TransportOrderId(value))
            .ValueGeneratedNever();

        // A Rejected booking has no number, so uniqueness only covers the rows that have one. EF never passes a null
        // to a value converter, which is why the conversions below can assume a value.
        builder.Property(order => order.Number)
            .HasConversion(number => number!.Value, value => TransportOrderNumber.Parse(value))
            .HasMaxLength(10);
        builder.HasIndex(order => order.Number).IsUnique();

        builder.Property(order => order.ShipperId)
            .HasConversion(id => id.Value, value => new ShipperId(value))
            .HasMaxLength(50)
            .IsRequired();

        // Only an Accepted Transport Order holds its Shipper reference, so this index is what guarantees that two
        // submissions of one booking, even at the same moment, never become two Transport Orders
        // (RetrySubmissionThatLostARace answers the second). The binary collation compares references exactly, as the
        // domain does.
        builder.Property(order => order.ShipperReference)
            .HasMaxLength(TransportOrderRules.MaxShipperReferenceLength)
            .UseCollation("Latin1_General_100_BIN2");
        builder.HasIndex(order => new { order.ShipperId, order.ShipperReference })
            .IsUnique()
            .HasDatabaseName(ShipperReferenceIndex);

        builder.Property(order => order.Status)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.HasIndex(order => new { order.Status, order.ReceivedAt });

        builder.Property(order => order.Channel)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(order => order.CorrelationId)
            .HasConversion(id => id.Value, value => new CorrelationId(value))
            .HasMaxLength(128)
            .IsRequired();

        // What the Shipper submitted is kept whole, as one JSON document: it's only ever read back as a whole.
        builder.Property(order => order.Details)
            .HasConversion(
                details => JsonSerializer.Serialize(details, DetailsJson),
                json => JsonSerializer.Deserialize<TransportOrderDetails>(json, DetailsJson)!,
                new ValueComparer<TransportOrderDetails>(
                    (left, right) => Equals(left, right),
                    details => details.GetHashCode(),
                    details => details))
            .HasColumnName("Details")
            .IsRequired();

        // The column is never null (an Accepted Transport Order stores []), so neither is what it holds.
        builder.Property(order => order.RejectionReasons)
            .HasConversion(
                reasons => JsonSerializer.Serialize(reasons, EnumNamesJson),
                json => JsonSerializer.Deserialize<RejectionReason[]>(json, EnumNamesJson)!,
                new ValueComparer<IReadOnlyList<RejectionReason>>(
                    (left, right) => left!.SequenceEqual(right!),
                    reasons => reasons.Count,
                    reasons => reasons.ToArray()))
            .IsRequired();

        // Rating changes a Transport Order after intake, so the TMS instances that rate must not both rate one: the
        // second save fails instead of publishing a second TransportOrderRated.
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.Property(order => order.RatingResult)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.HasIndex(order => new { order.RatingResult, order.ReceivedAt });

        // The Freight Charge is frozen whole, as one JSON document, like what the Shipper submitted.
        builder.Property(order => order.FreightCharge)
            .HasConversion(
                freightCharge => JsonSerializer.Serialize(freightCharge, FreightChargeJson.Options),
                json => JsonSerializer.Deserialize<FreightCharge>(json, FreightChargeJson.Options)!,
                new ValueComparer<FreightCharge?>(
                    (left, right) => Equals(left, right),
                    freightCharge => freightCharge == null ? 0 : freightCharge.GetHashCode(),
                    freightCharge => freightCharge));

        // A re-rate sent again under this key is a repeat, answered as before.
        builder.Property(order => order.LastReRateKey)
            .HasConversion(key => key!.Value, value => new IdempotencyKey(value))
            .HasMaxLength(IdempotencyKey.MaxLength);

        // The Transport Order's one Shipment, once it is Planned, and the key of the assignment that planned it, so that
        // assignment sent again is a repeat, answered as before.
        builder.Property(order => order.ShipmentNumber)
            .HasConversion(number => number!.Value, value => ShipmentNumber.Parse(value))
            .HasMaxLength(10);
        builder.Property(order => order.PlanningKey)
            .HasConversion(key => key!.Value, value => new IdempotencyKey(value))
            .HasMaxLength(IdempotencyKey.MaxLength);

        // Why Rating failed, kept like the rejection reasons: its stable code by name and its message.
        builder.Property(order => order.RatingFailure)
            .HasConversion(
                failure => JsonSerializer.Serialize(failure, EnumNamesJson),
                json => JsonSerializer.Deserialize<RatingFailure>(json, EnumNamesJson)!);
    }
}
