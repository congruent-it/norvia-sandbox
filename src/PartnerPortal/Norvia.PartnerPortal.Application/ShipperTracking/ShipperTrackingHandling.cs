using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.ShipperTracking;

internal static class ShipperTrackingHandling
{
    /// <summary>
    /// Asks Norvia as the Shipper Persona. A Persona that is none, as a Carrier has no tracking screen, is not found, and
    /// Norvia is not asked.
    /// </summary>
    public static async ValueTask<TrackingAnswer<T>> AskAsShipperAsync<T>(
        IPersonaDirectory personas, string personaId, Func<Persona, Task<TrackingAnswer<T>>> ask) =>
        personas.Find(new PersonaId(personaId)) is { PartyType: PartyType.Shipper } shipper
            ? await ask(shipper)
            : TrackingAnswer.NotFound<T>($"There is no Shipper Persona '{personaId}'.");
}
