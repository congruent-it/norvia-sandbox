namespace Norvia.Tms.Domain.TransportOrders;

/// <param name="Amount">Rounded to cents.</param>
public sealed record ChargeLine(ChargeLineType Type, Money Amount);
