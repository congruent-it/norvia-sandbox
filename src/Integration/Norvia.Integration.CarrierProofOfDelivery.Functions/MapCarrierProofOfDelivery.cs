using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.CarrierProofOfDelivery.Functions;

/// <summary>The Carrier Proof of delivery map as a local function of the Carrier Proof of delivery Logic App (ADR 0019).</summary>
public sealed class MapCarrierProofOfDelivery
{
    /// <param name="carrierProofOfDelivery">The Carrier's Proof of delivery as the blob's content, which the workflow passes as it read it: the Functions runtime would read the fields of JSON text as parameters (ADR 0019).</param>
    /// <param name="carrierId">The Carrier ID API Management took from the token and put on the message as <c>partyId</c>.</param>
    /// <returns>The TMS call's multipart body as JSON text: the Canonical Model metadata, then the decoded files.</returns>
    /// <exception cref="InvalidProofOfDeliveryException">The content isn't a Proof of delivery the map can read, which fails the action and so the run.</exception>
    [Function(nameof(MapCarrierProofOfDelivery))]
    public Task<string> RunAsync([WorkflowActionTrigger] string carrierProofOfDelivery, string carrierId) =>
        Task.FromResult(ProofOfDeliveryMultipart.Body(CarrierProofOfDeliveryMap.Map(MessageContent.Text(carrierProofOfDelivery), carrierId)).ToJsonString());
}
