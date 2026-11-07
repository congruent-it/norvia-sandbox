using Ardalis.Result;
using Mediator;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

/// <summary>The Carrier invoice matching agent's view of a whole Carrier invoice, found by the Carrier and the Carrier's invoice number.</summary>
public sealed record GetAgentInvoiceSummaryQuery(CarrierId CarrierId, string InvoiceNumber) : IQuery<Result<AgentInvoiceSummaryView>>;
