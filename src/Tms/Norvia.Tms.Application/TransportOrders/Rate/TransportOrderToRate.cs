namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>A Transport Order waiting for Rating, with the tracking-contract keys its rating is traced under.</summary>
public sealed record TransportOrderToRate(string TransportOrderNumber, string ShipperId, string CorrelationId);
