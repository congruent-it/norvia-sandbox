namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The Proof of delivery form a Driver fills in at a delivery Stop: who received the goods, whether with reservations (which then need a remark), the drawn
/// signature (a PNG) and the photos (PNGs, none or more). The files are made by the Simulator and the port only attaches them.
/// </summary>
public sealed record DriverDelivery(string ReceivedBy, bool Reservations, string? Remark, ReadOnlyMemory<byte> Signature, IReadOnlyList<ReadOnlyMemory<byte>> Photos);
