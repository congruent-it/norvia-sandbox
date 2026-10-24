using System.Globalization;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// A Carrier's answer in the Carrier answer format of the Norvia Partner API (contracts/partner-api/carrier-answer.v1.schema.json).
/// It never names the Carrier: Norvia takes it from the token (ADR 0005).
/// </summary>
internal static class CarrierAnswerFormat
{
    /// <summary>The reason code the unmapped-reason fault sends: a Carrier's own wording, outside the list the map reads.</summary>
    public const string UnmappedReasonCode = "CAPACITY_EXCEEDED";

    /// <param name="unmappedReason">Whether the unmapped-reason fault applies: a rejection then carries <see cref="UnmappedReasonCode"/>.</param>
    public static JsonObject ToJson(string carrierBookingNumber, CarrierAnswer answer, bool unmappedReason = false)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var json = new JsonObject { ["carrierBookingNumber"] = carrierBookingNumber };
        switch (answer)
        {
            case CarrierAnswer.Confirmation confirmation:
                json["decision"] = "CONFIRMED";
                json["bookingRef"] = confirmation.BookingReference;
                if (confirmation.PlannedCollection is { } planned)
                {
                    json["plannedCollection"] = planned.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);
                }

                break;
            case CarrierAnswer.Rejection rejection:
                json["decision"] = "DECLINED";
                json["declineReason"] = unmappedReason ? UnmappedReasonCode : ReasonCode(rejection.Reason);
                if (rejection.Text is { } text)
                {
                    json["declineText"] = text;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, "Every answer is a confirmation or a rejection.");
        }

        return json;
    }

    private static string ReasonCode(CarrierRejectionReason reason) => reason switch
    {
        CarrierRejectionReason.NoCapacity => "NO_CAPACITY",
        CarrierRejectionReason.LaneNotServed => "LANE_NOT_SERVED",
        CarrierRejectionReason.WindowNotFeasible => "WINDOW_NOT_FEASIBLE",
        CarrierRejectionReason.GoodsNotAccepted => "GOODS_NOT_ACCEPTED",
        CarrierRejectionReason.PriceNotAccepted => "PRICE_NOT_ACCEPTED",
        CarrierRejectionReason.Other => "OTHER",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Every reason has a Carrier answer code."),
    };
}
