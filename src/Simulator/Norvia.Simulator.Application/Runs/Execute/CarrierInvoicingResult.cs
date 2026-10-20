namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>What the Carrier invoicing stage came to: whether the run was cancelled, how many Carrier invoices Norvia took in, how many lines Finance decided, and how many line outcomes reached the Carriers' pages.</summary>
internal sealed record CarrierInvoicingResult(bool Stopped, int Invoiced, int Decided, int Received)
{
    public static CarrierInvoicingResult None { get; } = new(false, 0, 0, 0);
}
