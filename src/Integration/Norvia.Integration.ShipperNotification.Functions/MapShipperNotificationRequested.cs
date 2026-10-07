using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.ShipperNotification.Functions;

/// <summary>The Shipper notification map as a local function of the P7 Shipper notification Logic App (ADR 0019).</summary>
public sealed class MapShipperNotificationRequested
{
    /// <param name="shipperNotificationRequested">The <c>ShipperNotificationRequested</c> message's content as the trigger gives it (<see cref="MessageContent"/>).</param>
    /// <returns>The notification in the Shipper notification format as JSON text.</returns>
    /// <exception cref="InvalidOperationException">The message isn't a notification the Shipper can be told of. The action, and so the run, fails with the failure code in the message.</exception>
    [Function(nameof(MapShipperNotificationRequested))]
    public Task<string> RunAsync([WorkflowActionTrigger] string shipperNotificationRequested) =>
        Task.FromResult(
            ShipperNotificationMap.ToShipperNotification(MessageContent.Text(shipperNotificationRequested))?.ToJsonString()
            ?? throw new InvalidOperationException("ShipperNotificationRequestedUnreadable: the message isn't a notification the Shipper can be told of, so nothing was sent to the Shipper."));
}
