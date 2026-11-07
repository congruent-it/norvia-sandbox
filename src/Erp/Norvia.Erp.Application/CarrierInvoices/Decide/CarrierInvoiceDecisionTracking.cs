using Microsoft.Extensions.Logging;
using Norvia.Agents;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.CarrierInvoices.Decide;

internal static class CarrierInvoiceDecisionTracking
{
    /// <summary>
    /// Deciding a line carries on the Carrier invoice's own trail (coding guidelines §7): its correlation ID, <c>flow</c>, the Carrier as
    /// <c>partyId</c> and the Carrier's invoice number as <c>documentNumber</c>. A decision made from a Suggestion also names the Agent and its run (ADR 0040); one Finance made on
    /// their own names none.
    /// </summary>
    public static IDisposable? TrackDecision(this ILogger logger, CarrierInvoice invoice, AgentRun? actingOn)
    {
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = invoice.CorrelationId,
            [TrackingKeys.Flow] = Flows.CarrierInvoices,
            [TrackingKeys.PartyId] = invoice.CarrierId.Value,
            [TrackingKeys.DocumentNumber] = invoice.InvoiceNumber,
        };
        if (actingOn is not null)
        {
            keys[TrackingKeys.AgentId] = actingOn.AgentName;
            keys[TrackingKeys.AgentRunId] = actingOn.RunId;
        }

        return logger.Track(keys);
    }
}
