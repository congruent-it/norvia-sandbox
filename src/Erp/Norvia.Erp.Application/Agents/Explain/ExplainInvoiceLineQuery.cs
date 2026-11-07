using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.Agents.Explain;

/// <summary>Explain a Carrier invoice line that is To decide: the Carrier invoice matching agent says why it did not match, citing what it read.</summary>
public sealed record ExplainInvoiceLineQuery(CarrierId CarrierId, string InvoiceNumber, string CarrierBookingReference) : IQuery<Result<Suggestion>>;
