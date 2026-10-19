using Mediator;

namespace Norvia.Erp.Application.CarrierInvoices.List;

/// <summary>Every Received Carrier invoice, the oldest first, each line with its Carrier cost: what Finance decides from.</summary>
public sealed record ListCarrierInvoicesToDecideQuery : IQuery<IReadOnlyList<CarrierInvoiceToDecide>>;
