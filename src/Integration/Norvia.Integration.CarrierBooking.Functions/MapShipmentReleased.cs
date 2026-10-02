using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;

namespace Norvia.Integration.CarrierBooking.Functions;

/// <summary>The booking map as a local function of the P4 booking Logic App (ADR 0019).</summary>
public sealed class MapShipmentReleased
{
    /// <param name="shipmentReleased">The <c>ShipmentReleased</c> message's content as the trigger gives it (<see cref="MessageContent"/>).</param>
    /// <returns>The booking request in the Carrier booking format as JSON text.</returns>
    /// <exception cref="InvalidOperationException">The message isn't the release of a Carrier booking. The action, and so the run, fails with the failure code in the message.</exception>
    [Function(nameof(MapShipmentReleased))]
    public Task<string> RunAsync([WorkflowActionTrigger] string shipmentReleased) =>
        Task.FromResult(
            CarrierBookingMap.ToCarrierBooking(MessageContent.Text(shipmentReleased))?.ToJsonString()
            ?? throw new InvalidOperationException("ShipmentReleasedUnreadable: the message isn't the release of a Carrier booking, so nothing was sent to the Carrier."));
}
