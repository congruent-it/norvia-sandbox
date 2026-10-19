using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.CarrierInvoiceOutcome.Functions;

/// <summary>The Carrier invoice outcome map as a local function of the E2 Carrier invoice outcome Logic App (ADR 0019).</summary>
public sealed class MapCarrierInvoiceOutcome
{
    /// <param name="message">The <c>CarrierInvoiceApproved</c> message's content as the trigger gives it (<see cref="MessageContent"/>).</param>
    /// <returns>The outcome in the Carrier invoice outcome format as JSON text.</returns>
    /// <exception cref="InvalidOperationException">The message isn't an approved Carrier invoice the Carrier can be told. The action, and so the run, fails with the failure code in the message.</exception>
    [Function(nameof(MapCarrierInvoiceOutcome))]
    public Task<string> RunAsync([WorkflowActionTrigger] string message) =>
        Task.FromResult(
            CarrierInvoiceOutcomeMap.ToCarrierInvoiceOutcome(MessageContent.Text(message))?.ToJsonString()
            ?? throw new InvalidOperationException("CarrierInvoiceApprovedUnreadable: the message isn't an approved Carrier invoice the Carrier can be told, so nothing was sent to the Carrier."));
}
