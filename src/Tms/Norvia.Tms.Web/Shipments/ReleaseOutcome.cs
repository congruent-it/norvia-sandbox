namespace Norvia.Tms.Web.Shipments;

/// <summary>How one Shipment's release went, of the several a Planner released in one go.</summary>
/// <param name="Answer">What the TMS API answered; null when it couldn't be reached, so the Planner tries again.</param>
public sealed record ReleaseOutcome(string ShipmentNumber, ReleaseAnswer? Answer);
