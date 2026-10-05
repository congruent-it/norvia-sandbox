using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.CarrierTracking.Functions;

/// <summary>The Carrier-tracking map as a local function of the Carrier-tracking Logic App (ADR 0019).</summary>
public sealed class MapCarrierTracking
{
    /// <param name="carrierTracking">The Carrier's tracking message as the queue message's content, base64-encoded: the Functions runtime would read the fields of JSON text as parameters (ADR 0019).</param>
    /// <param name="carrierId">The Carrier ID API Management took from the token and put on the message as <c>partyId</c>.</param>
    /// <returns>The Canonical Model Tracking event as JSON text.</returns>
    /// <exception cref="UnknownStatusCodeException">The status code isn't in the code list, which fails the action and so the run.</exception>
    [Function(nameof(MapCarrierTracking))]
    public Task<string> RunAsync([WorkflowActionTrigger] string carrierTracking, string carrierId) =>
        Task.FromResult(CarrierTrackingMap.ToTrackingEvent(MessageContent.Text(carrierTracking), carrierId).ToJsonString());
}
