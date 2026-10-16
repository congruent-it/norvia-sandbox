namespace Norvia.Simulator.Application.Ports;

/// <summary>The Proof of delivery form a Driver fills in at a delivery Stop: who received the goods and whether with reservations, which then need a remark. The files are the port's to make.</summary>
public sealed record DriverDelivery(string ReceivedBy, bool Reservations, string? Remark);
