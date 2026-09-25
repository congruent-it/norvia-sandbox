namespace Norvia.PartnerPortal.Domain.Personas;

/// <summary>
/// One fictional party outside Norvia, a Shipper or a Carrier, as the Partner portal plays it, with its own identity
/// towards Norvia. The seed file names every Persona.
/// </summary>
/// <param name="PartyId">The party's ID at Norvia, such as <c>SHP-0001</c>. Norvia takes it from the Persona's token, never from what the portal sends (ADR 0005).</param>
/// <param name="SavedAddresses">The party's own address book, which demo bookings are filled in from.</param>
public sealed record Persona(PersonaId Id, string DisplayName, PartyType PartyType, string PartyId, IReadOnlyList<SavedAddress> SavedAddresses);
