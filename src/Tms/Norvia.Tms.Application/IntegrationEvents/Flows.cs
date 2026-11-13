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

    /// <summary>
    /// A Carrier's Tracking event (P5), recorded on the correlation ID API Management creates for it. The TMS stores it on
    /// the Tracking event, and the events it publishes carry it.
    /// </summary>
    public const string CarrierTracking = "carrier-tracking";

    /// <summary>
    /// A Carrier's Proof of delivery (P6), recorded on the correlation ID API Management creates for it. The TMS stores it
    /// on the Proof of delivery, and the event it publishes carries it.
    /// </summary>
    public const string CarrierProofOfDelivery = "carrier-proof-of-delivery";

    /// <summary>
    /// The Planner's resolution of an Exception (P7), which starts in the TMS UI and so gets a Message trail of its own. An
    /// Exception a Tracking event opens is traced on that event's trail, in <see cref="CarrierTracking"/>.
    /// </summary>
    public const string ExceptionHandling = "exception-handling";

    /// <summary>
    /// A Shipper's query of its Transport Orders (P8), on the correlation ID API Management creates for it. It changes nothing, so
    /// the correlation ID is only in the logs.
    /// </summary>
    public const string ShipperTracking = "shipper-tracking";

    /// <summary>
    /// Deciding that a Transport Order is Billable (P9), on the Message trail of the transaction that completed its last condition.
    /// </summary>
    public const string Billing = "billing";

    /// <summary>
    /// The Planner's building of Trips (P11), which starts in the TMS UI. Creating, changing and discarding a Draft publish nothing,
    /// so the trail is the TMS UI's hop and the TMS API's.
    /// </summary>
    public const string TripPlanning = "trip-planning";

    /// <summary>
    /// A Planner's dispatch of a Trip to its Driver (P11), which starts in the TMS UI and so gets a Message trail of its own that
    /// the Trip dispatch Logic App continues.
    /// </summary>
    public const string TripDispatch = "trip-dispatch";

    /// <summary>
    /// A Driver's work on a Dispatched Trip (P12): starting it, completing its Stops and ending it. It starts in the Driver app, whose
    /// correlation ID for each action the Driver reports Logic App continues.
    /// </summary>
    public const string TripExecution = "trip-execution";

    /// <summary>Master-data sync (E3): the ERP replicates a record Finance changed, on the correlation ID the ERP gave the change.</summary>
    public const string MasterData = "master-data";
}
