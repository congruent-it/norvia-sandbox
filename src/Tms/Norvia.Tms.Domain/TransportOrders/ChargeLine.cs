namespace Norvia.Tms.Domain.TransportOrders;

/// <param name="Amount">In EUR without VAT, rounded to cents.</param>
public sealed record ChargeLine(ChargeLineType Type, decimal Amount);
