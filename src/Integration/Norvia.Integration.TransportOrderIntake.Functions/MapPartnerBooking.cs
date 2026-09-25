using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;

namespace Norvia.Integration.TransportOrderIntake.Functions;

/// <summary>The intake map as a local function of the intake Logic App (ADR 0019).</summary>
public sealed class MapPartnerBooking
{
    /// <param name="partnerBooking">The booking as the JSON text the Shipper sent, base64-encoded (<see cref="EncodedBooking"/>).</param>
    /// <param name="shipperId">The Shipper, as API Management took it from the token (ADR 0005).</param>
    /// <returns>The Canonical Model Transport Order as JSON text.</returns>
    [Function(nameof(MapPartnerBooking))]
    public Task<string> RunAsync([WorkflowActionTrigger] string partnerBooking, string shipperId) =>
        Task.FromResult(IntakeMap.ToTransportOrder(EncodedBooking.Decode(partnerBooking), shipperId).ToJsonString());
}
