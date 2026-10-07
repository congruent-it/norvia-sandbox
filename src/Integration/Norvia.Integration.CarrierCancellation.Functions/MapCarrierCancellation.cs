using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.CarrierCancellation.Functions;

/// <summary>The Carrier cancellation map as a local function of the P7 Carrier cancellation Logic App (ADR 0019).</summary>
public sealed class MapCarrierCancellation
{
    /// <param name="carrierCancellationEvent">The <c>CarrierBookingCancelled</c> or <c>CarrierBookingWithdrawn</c> message's content as the trigger gives it (<see cref="MessageContent"/>).</param>
    /// <returns>The cancellation in the Carrier cancellation format as JSON text.</returns>
    /// <exception cref="InvalidOperationException">The message isn't a cancellation the Carrier can be told of. The action, and so the run, fails with the failure code in the message.</exception>
    [Function(nameof(MapCarrierCancellation))]
    public Task<string> RunAsync([WorkflowActionTrigger] string carrierCancellationEvent) =>
        Task.FromResult(
            CarrierCancellationMap.ToCarrierCancellation(MessageContent.Text(carrierCancellationEvent))?.ToJsonString()
            ?? throw new InvalidOperationException("CarrierCancellationUnreadable: the message isn't a cancellation the Carrier can be told of, so nothing was sent to the Carrier."));
}
