namespace Norvia.Simulator.Domain.Drivers;

public sealed record DriverFunnelCounts(int Shipments, int CollectionsFailed, int DeliveriesFailed, int Damaged, int Delivered, int WithReservations);
