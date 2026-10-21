using Azure.Monitor.Query.Logs;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// The KQL that finds the audit events the console wrote for a Message trail's messages (<c>OpenTelemetryAuditLog</c>): the
/// traces that carry the trail's correlation ID in their logging scope and, as the event's own properties, who did what to
/// which message. Only keys are read, never a message body.
/// </summary>
internal static class AuditQuery
{
    public static string For(string correlationId) =>
        LogsQueryClient.CreateQuery($"let searched = {correlationId};") + "\n" + Body;

    private const string Body = """
        AppTraces
        | where Properties has searched
        | where isnotempty(tostring(Properties.Actor)) and isnotempty(tostring(Properties.Action))
        | where tostring(Properties.correlationId) =~ searched
        | project at = TimeGenerated, actor = tostring(Properties.Actor), action = tostring(Properties.Action),
            entity = tostring(Properties.Entity), messageId = tostring(Properties.MessageId), reason = tostring(Properties.Reason),
            transportOrderNumber = tostring(Properties.transportOrderNumber), shipmentNumber = tostring(Properties.shipmentNumber),
            carrierBookingNumber = tostring(Properties.carrierBookingNumber), tripNumber = tostring(Properties.tripNumber),
            sourceMessageId = tostring(Properties.sourceMessageId)
        """;
}
