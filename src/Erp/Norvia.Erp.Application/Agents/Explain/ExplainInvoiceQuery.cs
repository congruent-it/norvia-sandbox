using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.Agents.Explain;

/// <summary>Explain a whole Carrier invoice: the Carrier invoice matching agent summarises the lines To decide by cause, citing what it read.</summary>
public sealed record ExplainInvoiceQuery(CarrierId CarrierId, string InvoiceNumber) : IQuery<Result<Suggestion>>;
