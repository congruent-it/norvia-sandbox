using Ardalis.Result;
using Mediator;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

/// <summary>The Carrier invoice matching agent's view of one Carrier invoice line, found by the Carrier, the Carrier's invoice number and the line's reference.</summary>
public sealed record GetAgentInvoiceLineQuery(CarrierId CarrierId, string InvoiceNumber, string CarrierBookingReference) : IQuery<Result<AgentInvoiceLineView>>;
