using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.CarrierAnswer.Functions;

/// <summary>The Carrier-answer map as a local function of the Carrier-answer Logic App (ADR 0019).</summary>
public sealed class MapCarrierAnswer
{
    /// <param name="carrierAnswer">The Carrier's answer as the JSON text the Carrier sent, base64-encoded: the Functions runtime would read the fields of JSON text as parameters (ADR 0019).</param>
    /// <param name="carrierId">The Carrier ID API Management took from the token.</param>
    /// <returns>The mapped answer as JSON text: the <c>carrierBookingNumber</c>, and the <c>tmsRequest</c> or the <c>problem</c> with the answer.</returns>
    [Function(nameof(MapCarrierAnswer))]
    public Task<string> RunAsync([WorkflowActionTrigger] string carrierAnswer, string carrierId) =>
        Task.FromResult(CarrierAnswerMap.ToCarrierBookingAnswer(MessageContent.Text(carrierAnswer), carrierId).ToJsonString());
}
