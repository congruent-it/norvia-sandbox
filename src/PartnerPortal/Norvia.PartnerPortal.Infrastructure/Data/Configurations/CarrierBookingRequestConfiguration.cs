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

        // The words of the answer, as enum names that stay readable in the table and survive a reordering of the enums.
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(request => request.AnswerCorrelationId).HasMaxLength(200);
        builder.Property(request => request.CarrierBookingReference).HasMaxLength(200);
        builder.Property(request => request.RejectionReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(request => request.LastProgress).HasConversion<string>().HasMaxLength(30);
        builder.Ignore(request => request.Stage);
        builder.Property(request => request.RejectionText).HasMaxLength(1000);
        builder.Property(request => request.CancellationText).HasMaxLength(500);

        // The automatic answer the request arrived with: the policy then in force, which later changes to it don't touch.
        builder.Property(request => request.AutoAnswerMode).HasConversion<string>().HasMaxLength(30);
        builder.Property(request => request.AutoAnswerReason).HasConversion<string>().HasMaxLength(30);
        builder.HasIndex(request => request.AutoAnswerDueAt);

        // The automatic Tracking events a confirmed Shipment goes on with: the delay then in force, and when the next is due.
        builder.Property(request => request.AutoProgressDelay)
            .HasColumnName("AutoProgressDelayMilliseconds")
            .HasConversion(delay => delay == null ? (long?)null : (long)delay.Value.TotalMilliseconds, value => value == null ? (TimeSpan?)null : TimeSpan.FromMilliseconds(value.Value));
        builder.HasIndex(request => request.AutoProgressDueAt);

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
