namespace Norvia.DriverApp.Domain.Reports;

/// <summary>
/// What the Driver reported (P12). Collected, Collection failed and Delivery failed complete a Stop; Damaged is reported on any Stop and doesn't.
/// The other reports of a Driver (a Delivered with its Proof of delivery, the end of the Trip) join as they are built.
/// </summary>
public enum ReportKind
{
    StartTrip,
    Collected,
    CollectionFailed,
    DeliveryFailed,
    Damaged,
}
