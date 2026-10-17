using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.Shipments;

/// <summary>
/// Explains why a Shipment to release can't be priced from its Carrier's Carrier Rate Card, in the glossary's words, for
/// a Planner deciding on a Spot price. Not <see cref="Norvia.Tms.Web.TransportOrders.RatingText.Failure"/>, which is the
/// wording for Customer service.
/// </summary>
public static class PricingFailureText
{
    /// <summary>
    /// One plain sentence for the failure code, followed by the Carrier. A code the UI doesn't know yet falls back to the
    /// readable message the TMS sent, which names the Carrier already.
    /// </summary>
    public static string Of(PricingFailureView failure, CarriedByView carriedBy)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(carriedBy);
        var sentence = failure.Code switch
        {
            nameof(RatingFailureCode.NoRateCard) => "No Carrier Rate Card for this Carrier.",
            nameof(RatingFailureCode.NoValidRateCardVersion) => "The Carrier Rate Card has no valid version for this date.",
            nameof(RatingFailureCode.NoMatchingLane) => "The Carrier Rate Card has no Lane for this origin and destination.",
            nameof(RatingFailureCode.WeightAboveHighestBreak) => "The weight is above the highest Weight Break on the Lane.",
            _ => null,
        };
        return sentence is null ? failure.Message : $"{sentence} Carrier: {PlanningText.CarriedBy(carriedBy)}.";
    }
}
