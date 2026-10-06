using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierShipments;

/// <summary>Words for a Proof of delivery's condition, as the Carrier's own codes name it.</summary>
public static class ProofOfDeliveryText
{
    public static string Condition(ProofOfDeliveryCondition condition) => condition switch
    {
        ProofOfDeliveryCondition.Clean => "Clean (CLEAN)",
        ProofOfDeliveryCondition.Reservations => "With reservations (RESERVATIONS)",
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Every condition has words."),
    };
}
