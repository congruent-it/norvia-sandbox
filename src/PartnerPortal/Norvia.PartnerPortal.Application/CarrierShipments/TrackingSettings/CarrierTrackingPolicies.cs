using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;

internal static class CarrierTrackingPolicies
{
    /// <summary>The policy in force for a Carrier Persona: what the presenter chose, or else the seed file's default.</summary>
    public static CarrierTrackingPolicy InForce(Persona carrier, CarrierTrackingSettings? chosen) =>
        chosen?.Policy ?? carrier.Carrier!.DefaultTrackingPolicy;
}
