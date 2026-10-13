namespace Norvia.DriverApp.Domain.Reports;

/// <summary>The two reports of one Deliver: the Delivered that completes the Stop, and the Proof of delivery.</summary>
public sealed record DeliveredReports(DriverReport Delivered, DriverReport ProofOfDelivery);
