namespace Norvia.PartnerPortal.Application.Faults;

/// <summary>The names of the faults the Partner portal owns, as the Carriers and Shippers it plays. The presenter panel switches them, and a hop one caused carries the name as its <c>fault</c> key.</summary>
public static class PartnerPortalFaultNames
{
    /// <summary>P5: while on, a Carrier's Delivered event carries the status code <c>DLV</c> instead of <c>POD</c>, which the Carrier-tracking map can't read.</summary>
    public const string TrackingUnmappedCode = "carrier-tracking-unmapped-code";

    /// <summary>P4: while on, a Carrier's rejection carries a reason code the Carrier-answer map can't read.</summary>
    public const string AnswerUnmappedReason = "carrier-answer-unmapped-reason";

    /// <summary>P4: while on, a Carrier's booking endpoint answers 503 and keeps nothing.</summary>
    public const string BookingEndpointDown = "carrier-booking-endpoint-down";

    /// <summary>P6: while on, a Carrier's Proof of delivery carries a <c>.heic</c> photo, which the TMS refuses.</summary>
    public const string ProofHeicPhoto = "carrier-proof-heic-photo";

    /// <summary>P7: while on, a Shipper's notification endpoint answers 503 and keeps nothing.</summary>
    public const string ShipperEndpointDown = "shipper-notification-endpoint-down";

    /// <summary>P7: while on, a Carrier's cancellation endpoint answers 503 and keeps nothing.</summary>
    public const string CancellationEndpointDown = "carrier-cancellation-endpoint-down";

    /// <summary>P12: while on, the Driver reports Logic App is disabled in Azure (locally its worker is stopped), so the Drivers' reports stay Sending. The only fault that is Azure state rather than application code.</summary>
    public const string DriverReportsLogicAppDisabled = "driver-reports-logic-app-disabled";
}
