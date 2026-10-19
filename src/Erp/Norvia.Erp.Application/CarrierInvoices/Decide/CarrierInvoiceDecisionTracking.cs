using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.CarrierInvoices.Decide;

internal static class CarrierInvoiceDecisionTracking
{
    /// <summary>
    /// Deciding a line carries on the Carrier invoice's own trail (coding guidelines §7): its correlation ID, <c>flow</c>, the Carrier as
    /// <c>partyId</c> and the Carrier's invoice number as <c>documentNumber</c>.
    /// </summary>
    public static IDisposable? TrackDecision(this ILogger logger, CarrierInvoice invoice) =>
        logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = invoice.CorrelationId,
            [TrackingKeys.Flow] = Flows.CarrierInvoices,
            [TrackingKeys.PartyId] = invoice.CarrierId.Value,
            [TrackingKeys.DocumentNumber] = invoice.InvoiceNumber,
        });
}
