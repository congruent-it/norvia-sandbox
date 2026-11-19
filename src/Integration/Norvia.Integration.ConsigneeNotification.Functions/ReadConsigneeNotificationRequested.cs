using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.ConsigneeNotification.Functions;

/// <summary>Reads a Consignee notification request as a local function of the P7 Consignee notification Logic App (ADR 0019).</summary>
public sealed class ReadConsigneeNotificationRequested
{
    /// <param name="consigneeNotificationRequested">The <c>ConsigneeNotificationRequested</c> message's content as the trigger gives it (<see cref="MessageContent"/>).</param>
    /// <returns>The Transport Order and the reason as JSON text.</returns>
    /// <exception cref="InvalidOperationException">The message isn't a notification the Consignee can be told of. The action, and so the run, fails with the failure code in the message.</exception>
    [Function(nameof(ReadConsigneeNotificationRequested))]
    public Task<string> RunAsync([WorkflowActionTrigger] string consigneeNotificationRequested) =>
        Task.FromResult(
            ConsigneeNotificationMap.ToNotification(MessageContent.Text(consigneeNotificationRequested))?.ToJsonString()
            ?? throw new InvalidOperationException("ConsigneeNotificationRequestedUnreadable: the message isn't a notification the Consignee can be told of, so nothing was sent to the Consignee."));
}
