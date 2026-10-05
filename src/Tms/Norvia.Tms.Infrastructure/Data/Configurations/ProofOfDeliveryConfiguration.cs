using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.Data.Configurations;

internal sealed class ProofOfDeliveryConfiguration : IEntityTypeConfiguration<ProofOfDelivery>
{
    private const string OwnFleetMarker = "OWN-FLEET";
    private static readonly JsonSerializerOptions FilesJson = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ProofOfDelivery> builder)
    {
        builder.ToTable("ProofsOfDelivery");
        builder.Ignore(proofOfDelivery => proofOfDelivery.DomainEvents);

        builder.HasKey(proofOfDelivery => proofOfDelivery.Id);
        builder.Property(proofOfDelivery => proofOfDelivery.Id)
            .HasConversion(id => id.Value, value => new ProofOfDeliveryId(value))
            .ValueGeneratedNever();

        // A Shipment's Proofs of delivery: the current one and, later, the ones it replaced. Never deleted with it.
        builder.Property(proofOfDelivery => proofOfDelivery.ShipmentId)
            .HasConversion(id => id.Value, value => new ShipmentId(value));
        builder.HasOne<Shipment>().WithMany().HasForeignKey(proofOfDelivery => proofOfDelivery.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(proofOfDelivery => proofOfDelivery.ShipmentId);

        builder.Property(proofOfDelivery => proofOfDelivery.ShipmentNumber)
            .HasConversion(number => number.Value, value => ShipmentNumber.Parse(value))
            .HasMaxLength(10)
            .IsRequired();

        // Who sent it is one column, so a source's document ID can be unique: the Carrier's ID, or a marker for Own fleet.
        builder.Property(proofOfDelivery => proofOfDelivery.Source)
            .HasConversion(
                source => source.CarrierId == null ? OwnFleetMarker : source.CarrierId.Value,
                value => value == OwnFleetMarker ? ProofOfDeliverySource.OwnFleet : ProofOfDeliverySource.Carrier(new CarrierId(value)))
            .HasColumnName("SourceId")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(proofOfDelivery => proofOfDelivery.DocumentId).HasMaxLength(100).IsRequired();

        // A source's own document ID is unique, so a Proof of delivery sent twice is one, even when both calls race.
        builder.HasIndex(proofOfDelivery => new { proofOfDelivery.Source, proofOfDelivery.DocumentId }).IsUnique();

        builder.Property(proofOfDelivery => proofOfDelivery.CarrierBookingReference).HasMaxLength(50).IsRequired();
        builder.Property(proofOfDelivery => proofOfDelivery.ReceivedBy).HasMaxLength(200).IsRequired();
        builder.Property(proofOfDelivery => proofOfDelivery.DeliveryTime).IsRequired();

        builder.ComplexProperty(proofOfDelivery => proofOfDelivery.Location, location =>
        {
            location.Property(l => l.Place).HasMaxLength(100).HasColumnName("LocationPlace");
            location.Property(l => l.Postcode).HasMaxLength(20).HasColumnName("LocationPostcode");
            location.Property(l => l.CountryCode).HasMaxLength(2).HasColumnName("LocationCountryCode");
        });

        builder.Property(proofOfDelivery => proofOfDelivery.Condition).HasConversion<string>().HasMaxLength(20);
        builder.Property(proofOfDelivery => proofOfDelivery.Remark).HasMaxLength(500);

        // The files' descriptions are read and written only with their Proof of delivery, so they are one JSON column.
        builder.Property(proofOfDelivery => proofOfDelivery.Files)
            .HasConversion(
                files => JsonSerializer.Serialize(files, FilesJson),
                json => JsonSerializer.Deserialize<ProofOfDeliveryFile[]>(json, FilesJson)!,
                new ValueComparer<IReadOnlyList<ProofOfDeliveryFile>>(
                    (left, right) => left!.SequenceEqual(right!),
                    files => files.Count,
                    files => files.ToArray()))
            .IsRequired();

        builder.Property(proofOfDelivery => proofOfDelivery.CorrelationId)
            .HasConversion(id => id.Value, value => new CorrelationId(value))
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(proofOfDelivery => proofOfDelivery.ReceivedAt).IsRequired();
        builder.Property(proofOfDelivery => proofOfDelivery.IsCurrent);
    }
}
