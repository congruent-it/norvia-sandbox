using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;

internal static class CarrierAnswerPolicies
{
    /// <summary>The policy in force for a Carrier Persona: what the presenter chose, or else the seed file's default.</summary>
    public static CarrierAnswerPolicy InForce(Persona carrier, CarrierAnswerSettings? chosen) =>
        chosen?.Policy ?? carrier.Carrier!.DefaultAnswerPolicy;
}
