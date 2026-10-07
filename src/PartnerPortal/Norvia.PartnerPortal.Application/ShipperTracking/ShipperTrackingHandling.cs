using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.ShipperTracking;

internal static class ShipperTrackingHandling
{
    /// <summary>The Shipper Persona; null for a Persona that is none, as a Carrier has no tracking screen.</summary>
    public static Persona? ShipperOf(IPersonaDirectory personas, string personaId) =>
        personas.Find(new PersonaId(personaId)) is { PartyType: PartyType.Shipper } persona ? persona : null;

    public static TrackingAnswer<T> NoShipper<T>(string personaId) => TrackingAnswer.NotFound<T>($"There is no Shipper Persona '{personaId}'.");
}
