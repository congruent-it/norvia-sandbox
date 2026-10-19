using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.ShipperInvoice.Functions;

/// <summary>The Shipper invoice map as a local function of the E1 Shipper invoice Logic App (ADR 0019), for invoices and Credit notes alike.</summary>
public sealed class MapShipperDocument
{
    /// <param name="message">The <c>ShipperInvoiceIssued</c> or <c>CreditNoteIssued</c> message's content as the trigger gives it (<see cref="MessageContent"/>).</param>
    /// <returns>The invoice or Credit note in the Shipper invoice format as JSON text.</returns>
    /// <exception cref="InvalidOperationException">The message isn't an invoice or Credit note the Shipper can be sent. The action, and so the run, fails with the failure code in the message.</exception>
    [Function(nameof(MapShipperDocument))]
    public Task<string> RunAsync([WorkflowActionTrigger] string message) =>
        Task.FromResult(
            ShipperInvoiceMap.ToShipperDocument(MessageContent.Text(message))?.ToJsonString()
            ?? throw new InvalidOperationException("ShipperInvoiceIssuedUnreadable: the message isn't an invoice or Credit note the Shipper can be sent, so nothing was sent to the Shipper."));
}
