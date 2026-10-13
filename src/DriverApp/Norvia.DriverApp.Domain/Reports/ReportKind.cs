namespace Norvia.DriverApp.Domain.Reports;

/// <summary>
/// What the Driver reported (P12). Collected, Collection failed, Delivered and Delivery failed complete a Stop; Damaged is reported on any Stop and doesn't.
/// A Proof of delivery is reported with the Delivered, as a report of its own because the TMS records it apart from the Tracking event and may refuse it
/// on its own; it doesn't complete the Stop. The end of the Trip joins as it is built.
/// </summary>
public enum ReportKind
{
    StartTrip,
    Collected,
    CollectionFailed,
    DeliveryFailed,
    Damaged,
    Delivered,
    ProofOfDelivery,
}
