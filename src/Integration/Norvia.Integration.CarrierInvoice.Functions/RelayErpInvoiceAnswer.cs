using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.CarrierInvoice.Functions;

/// <summary>Relays the ERP's response to the Carrier in the Carrier invoice response format, as a local function of the Carrier invoice Logic App.</summary>
public sealed class RelayErpInvoiceAnswer
{
    /// <param name="mappedInvoice">What <see cref="MapCarrierInvoice"/> returned.</param>
    /// <param name="erpStatusCode">The status the ERP answered with, or 0 when it didn't answer or wasn't called.</param>
    /// <param name="erpResponse">The ERP's answer as JSON text, or empty when it didn't answer.</param>
    /// <param name="correlationId">The correlation ID of the invoice.</param>
    /// <returns>The answer for the Carrier as JSON text: its <c>statusCode</c>, <c>body</c> and, when it wasn't received for a reason the run should show, <c>failureCode</c>.</returns>
    [Function(nameof(RelayErpInvoiceAnswer))]
    public Task<string> RunAsync([WorkflowActionTrigger] string mappedInvoice, int erpStatusCode, string erpResponse, string correlationId) =>
        Task.FromResult(CarrierInvoiceResponses.Relay(MappedCarrierInvoice.FromJson(mappedInvoice), erpStatusCode, erpResponse, correlationId).ToJsonString());
}
