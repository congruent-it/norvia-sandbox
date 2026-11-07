namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

/// <param name="Source">Where the price came from, as a label: <c>Rate Card version 3</c> or <c>Spot</c>. Never the Planner's remark.</param>
/// <param name="DeliveredOn">The day the Shipment was Delivered.</param>
public sealed record AgentCarrierCostView(
    string CarrierBookingNumber,
    string CarrierBookingReference,
    decimal Total,
    string Source,
    DateOnly DeliveredOn,
    string Status);
