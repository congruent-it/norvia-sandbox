namespace Norvia.PartnerPortal.Domain.Personas;

/// <summary>
/// One fictional party outside Norvia, a Shipper or a Carrier, as the Partner portal plays it, with its own identity
/// towards Norvia. The seed file names every Persona.
/// </summary>
/// <param name="PartyId">The party's ID at Norvia, such as <c>SHP-0001</c>. Norvia takes it from the Persona's token, never from what the portal sends (ADR 0005).</param>
/// <param name="SavedAddresses">The party's own address book, which demo bookings are filled in from.</param>
/// <param name="Carrier">What only a Carrier Persona has; null for a Shipper.</param>
/// <param name="Shipper">What only a Shipper Persona has; null for a Carrier, and for a Shipper the seed file says nothing more of.</param>
public sealed record Persona(
    PersonaId Id, string DisplayName, PartyType PartyType, string PartyId, IReadOnlyList<SavedAddress> SavedAddresses, CarrierProfile? Carrier = null, ShipperProfile? Shipper = null)
{
    /// <summary>How often the Persona's tracking screen asks Norvia again while it is open.</summary>
    public TimeSpan TrackingPollInterval => Shipper?.TrackingPollInterval ?? ShipperProfile.DefaultTrackingPollInterval;
}
