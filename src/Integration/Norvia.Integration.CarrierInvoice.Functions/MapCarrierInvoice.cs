using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.CarrierInvoice.Functions;

/// <summary>The Carrier invoice map as a local function of the Carrier invoice Logic App (ADR 0019).</summary>
public sealed class MapCarrierInvoice
{
    /// <param name="carrierInvoice">The Carrier's invoice as the JSON text the Carrier sent, base64-encoded: the Functions runtime would read the fields of JSON text as parameters (ADR 0019).</param>
    /// <param name="carrierId">The Carrier ID API Management took from the token.</param>
    /// <param name="correlationId">The correlation ID API Management created for the invoice.</param>
    /// <returns>The mapped invoice as JSON text: the <c>invoiceNumber</c>, and the <c>erpRequest</c> or the <c>problem</c> with the invoice.</returns>
    [Function(nameof(MapCarrierInvoice))]
    public Task<string> RunAsync([WorkflowActionTrigger] string carrierInvoice, string carrierId, string correlationId) =>
        Task.FromResult(CarrierInvoiceMap.ToCarrierInvoiceSubmission(MessageContent.Text(carrierInvoice), carrierId, correlationId).ToJsonString());
}
