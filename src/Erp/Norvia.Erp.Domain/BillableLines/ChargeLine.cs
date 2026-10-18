namespace Norvia.Erp.Domain.BillableLines;

/// <summary>One part of a Freight Charge, in EUR without VAT and rounded to cents.</summary>
public sealed record ChargeLine(ChargeLineType Type, decimal Amount);
