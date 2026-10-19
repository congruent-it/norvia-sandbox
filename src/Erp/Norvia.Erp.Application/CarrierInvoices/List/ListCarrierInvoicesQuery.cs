using Mediator;

namespace Norvia.Erp.Application.CarrierInvoices.List;

/// <summary>Every Carrier invoice, the newest first, with its status and approved amount.</summary>
public sealed record ListCarrierInvoicesQuery : IQuery<IReadOnlyList<CarrierInvoiceListItem>>;
