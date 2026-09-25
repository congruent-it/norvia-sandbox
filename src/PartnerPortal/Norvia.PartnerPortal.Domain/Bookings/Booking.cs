using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.Bookings;

/// <summary>
/// A booking a Persona sent Norvia through the Norvia Partner API, with Norvia's answer: the outside party's own record
/// of it. Norvia owns the Transport Order; the Partner portal keeps only what the party sent and heard back.
/// </summary>
public sealed class Booking : EntityBase<BookingId>, IAggregateRoot
{
    private Booking(BookingId id, PersonaId personaId, string? shipperReference, DateTimeOffset sentAt, NorviaAnswer answer)
    {
        Id = id;
        PersonaId = personaId;
        ShipperReference = shipperReference;
        SentAt = sentAt;
        Answer = answer;
    }

    public PersonaId PersonaId { get; private set; }

    /// <summary>The party's own reference for the booking; null when it sent none, which Norvia rejects.</summary>
    public string? ShipperReference { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    public NorviaAnswer Answer { get; private set; }

    /// <summary>Records a booking once Norvia has answered, whatever the answer: every attempt is on the party's record.</summary>
    public static Booking Record(PersonaId personaId, string? shipperReference, DateTimeOffset sentAt, NorviaAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        ArgumentNullException.ThrowIfNull(answer);
        return new Booking(BookingId.New(), personaId, shipperReference, sentAt, answer);
    }
}
