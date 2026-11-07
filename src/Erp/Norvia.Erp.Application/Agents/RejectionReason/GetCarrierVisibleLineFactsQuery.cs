using Ardalis.Result;
using Mediator;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.Agents.RejectionReason;

/// <summary>The rejection reason agent's view of one Carrier invoice line: only what the Carrier can see of it.</summary>
public sealed record GetCarrierVisibleLineFactsQuery(CarrierId CarrierId, string InvoiceNumber, string CarrierBookingReference) : IQuery<Result<CarrierVisibleLineFactsView>>;
