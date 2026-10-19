namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>What the invoicing stage came to: whether the run was cancelled, how many invoices and Credit notes Finance issued, and how many documents reached the Shippers' inboxes.</summary>
internal sealed record InvoicingResult(bool Stopped, int Invoiced, int Credited, int Received)
{
    public static InvoicingResult None { get; } = new(false, 0, 0, 0);
}
