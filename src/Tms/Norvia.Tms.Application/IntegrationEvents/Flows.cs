namespace Norvia.Tms.Application.IntegrationEvents;

/// <summary>Stable flow names of the tracking contract (coding guidelines §7).</summary>
public static class Flows
{
    public const string TransportOrderIntake = "transport-order-intake";

    /// <summary>Rating an accepted Transport Order (P2), which continues the Message trail of its intake.</summary>
    public const string TransportOrderRating = "transport-order-rating";

    /// <summary>
    /// A Planner's action on a Transport Order or Shipment (P3), which starts in the TMS UI and so gets a Message trail of
    /// its own.
    /// </summary>
    public const string TransportPlanning = "transport-planning";

    /// <summary>The Carrier's answer to a Carrier booking (P4), on the correlation ID the answer's own caller passes.</summary>
    public const string CarrierBookingAnswer = "carrier-booking-answer";

    /// <summary>
    /// The P4 booking Logic App's report of a Carrier booking as sent or not delivered, which continues the Message
    /// trail of the release that created it.
    /// </summary>
    public const string CarrierBookingReport = "carrier-booking-report";
}
