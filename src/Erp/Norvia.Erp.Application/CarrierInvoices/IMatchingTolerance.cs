namespace Norvia.Erp.Application.CarrierInvoices;

/// <summary>The ERP's Matching tolerance setting: how far a Carrier invoice line's amount may be from its Carrier cost and still match.</summary>
public interface IMatchingTolerance
{
    /// <summary>In EUR, never negative. €0 by default: the amount must equal the Carrier cost to the cent.</summary>
    decimal Amount { get; }
}
