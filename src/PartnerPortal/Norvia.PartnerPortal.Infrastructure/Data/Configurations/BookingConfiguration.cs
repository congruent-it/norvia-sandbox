using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.Data.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    /// <summary>The Norvia Partner API's limit (booking.v1.schema.json), which the booking form keeps to.</summary>
    public const int MaxShipperReferenceLength = 100;

    private static readonly JsonSerializerOptions AnswerJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.Ignore(booking => booking.DomainEvents);

        builder.HasKey(booking => booking.Id);
        builder.Property(booking => booking.Id)
            .HasConversion(id => id.Value, value => new BookingId(value))
            .ValueGeneratedNever();

        builder.Property(booking => booking.PersonaId)
            .HasConversion(id => id.Value, value => new PersonaId(value))
            .HasMaxLength(100)
            .IsRequired();
        builder.HasIndex(booking => new { booking.PersonaId, booking.SentAt });

        builder.Property(booking => booking.ShipperReference).HasMaxLength(MaxShipperReferenceLength);

        // Norvia's answer is kept whole, as one JSON document: it's only ever read back as a whole.
        builder.Property(booking => booking.Answer)
            .HasConversion(
                answer => JsonSerializer.Serialize(AnswerDocument.From(answer), AnswerJson),
                json => JsonSerializer.Deserialize<AnswerDocument>(json, AnswerJson)!.ToAnswer(),
                new ValueComparer<NorviaAnswer>(
                    (left, right) => Equals(left, right),
                    answer => answer.GetHashCode(),
                    answer => answer))
            .IsRequired();
    }

    /// <summary>The answer as it is stored. The domain builds a <see cref="NorviaAnswer"/> only through its factories, which check it.</summary>
    private sealed record AnswerDocument(
        BookingResult Result,
        string? MessageId,
        string? TransportOrderNumber,
        IReadOnlyList<RejectionReason>? Reasons,
        string? Text)
    {
        public static AnswerDocument From(NorviaAnswer answer) =>
            new(answer.Result, answer.MessageId, answer.TransportOrderNumber, answer.Reasons.Count > 0 ? answer.Reasons : null, answer.Text);

        public NorviaAnswer ToAnswer() => Result switch
        {
            BookingResult.Accepted => NorviaAnswer.Accepted(TransportOrderNumber!, MessageId),
            BookingResult.AlreadyBooked => NorviaAnswer.AlreadyBooked(TransportOrderNumber!, MessageId),
            BookingResult.Rejected => NorviaAnswer.Rejected(Reasons ?? [], MessageId),
            BookingResult.NotReceived => NorviaAnswer.NotReceived(MessageId),
            BookingResult.Refused => NorviaAnswer.Refused(Text!, MessageId),
            _ => throw new InvalidDataException($"A stored answer has the unknown result {Result}."),
        };
    }
}
