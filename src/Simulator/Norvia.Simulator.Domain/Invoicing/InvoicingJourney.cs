namespace Norvia.Simulator.Domain.Invoicing;

/// <summary>What Finance does for one Shipper of the run: whether it runs Invoice now, and whether it then credits part of one line of the invoice.</summary>
public sealed record InvoicingJourney(int Index, bool Invoice, bool CreditNote);
