using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.Data.Configurations;

internal sealed class CarrierBookingRequestConfiguration : IEntityTypeConfiguration<CarrierBookingRequest>
{
    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<CarrierBookingRequest> builder)
    {
        builder.ToTable("CarrierBookingRequests");
        builder.Ignore(request => request.DomainEvents);

        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id)
            .HasConversion(id => id.Value, value => new CarrierBookingRequestId(value))
            .ValueGeneratedNever();

        builder.Property(request => request.PersonaId)
            .HasConversion(id => id.Value, value => new PersonaId(value))
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(request => request.CarrierBookingNumber).HasMaxLength(50).IsRequired();
        builder.Property(request => request.ShipmentNumber).HasMaxLength(50).IsRequired();
        builder.Property(request => request.CorrelationId).HasMaxLength(200).IsRequired();

        // The Carrier booking number is the key of a Persona's inbox, so a request that arrives twice is stored once.
        builder.HasIndex(request => new { request.PersonaId, request.CarrierBookingNumber }).IsUnique();
        builder.HasIndex(request => new { request.PersonaId, request.ReceivedAt });

        // What the request asks is kept whole, as one JSON document: it's only ever read back as a whole.
        builder.Property(request => request.Details)
            .HasConversion(
                details => JsonSerializer.Serialize(details, DetailsJson),
                json => JsonSerializer.Deserialize<CarrierBookingDetails>(json, DetailsJson)!,
                new ValueComparer<CarrierBookingDetails>(
                    (left, right) => JsonSerializer.Serialize(left, DetailsJson) == JsonSerializer.Serialize(right, DetailsJson),
                    details => JsonSerializer.Serialize(details, DetailsJson).GetHashCode(StringComparison.Ordinal),
                    details => JsonSerializer.Deserialize<CarrierBookingDetails>(JsonSerializer.Serialize(details, DetailsJson), DetailsJson)!))
            .IsRequired();
    }
}
