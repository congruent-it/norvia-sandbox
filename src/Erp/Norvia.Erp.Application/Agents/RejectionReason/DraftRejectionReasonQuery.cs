using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.Agents.RejectionReason;

/// <summary>Ask the rejection reason agent for an editable, Carrier-facing reason for rejecting a Carrier invoice line. A Suggestion: the ERP sends nothing and stores nothing.</summary>
public sealed record DraftRejectionReasonQuery(CarrierId CarrierId, string InvoiceNumber, string CarrierBookingReference) : IQuery<Result<Suggestion>>;
