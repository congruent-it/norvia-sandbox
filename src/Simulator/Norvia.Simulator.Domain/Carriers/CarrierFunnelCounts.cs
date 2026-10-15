namespace Norvia.Simulator.Domain.Carriers;

/// <summary>How many Shipments reach each step of the Carrier stage.</summary>
public sealed record CarrierFunnelCounts(
    int Shipments,
    int Released,
    int Confirmed,
    int Declined,
    int NoAnswer,
    int Tracked,
    int Exceptions,
    int Delivered,
    int ProofsOfDelivery,
    int Cancelled);
