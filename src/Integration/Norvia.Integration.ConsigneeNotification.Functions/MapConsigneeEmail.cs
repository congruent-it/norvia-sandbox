using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.ConsigneeNotification.Functions;

/// <summary>The Consignee notification map as a local function of the P7 Consignee notification Logic App (ADR 0019).</summary>
public sealed class MapConsigneeEmail
{
    /// <param name="consigneeNotificationRequested">The <c>ConsigneeNotificationRequested</c> message's content as the trigger gives it.</param>
    /// <param name="consigneeNotificationContent">What the TMS gave for the Transport Order: its <c>email</c> and <c>trackingCode</c>.</param>
    /// <param name="trackingBaseUrl">The Partner portal's address.</param>
    /// <returns>The email as JSON text: <c>to</c>, <c>subject</c> and <c>plainText</c>.</returns>
    /// <exception cref="InvalidOperationException">The TMS's answer or the address can't be used, so nothing is sent. The message never holds the address or the code.</exception>
    [Function(nameof(MapConsigneeEmail))]
    public Task<string> RunAsync([WorkflowActionTrigger] string consigneeNotificationRequested, string consigneeNotificationContent, string trackingBaseUrl) =>
        Task.FromResult(
            ConsigneeNotificationMap.ToEmail(MessageContent.Text(consigneeNotificationRequested), consigneeNotificationContent, trackingBaseUrl)?.ToJsonString()
            ?? throw new InvalidOperationException("ConsigneeEmailUnusable: the Consignee's address or the tracking link can't be used, so nothing was sent to the Consignee."));
}
