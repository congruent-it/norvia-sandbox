using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Api.CarrierBookings;

/// <summary>
/// The Carrier's answer to a Carrier booking (P4): a confirmation carries <see cref="CarrierBookingReference"/>
/// (required) and, optionally, <see cref="PlannedCollectionTime"/>; a rejection carries <see cref="ReasonCode"/>
/// (required) and, optionally, <see cref="ReasonText"/>.
/// </summary>
/// <param name="CarrierId">
/// The Carrier answering. API Management's token check will take it from the token (ADR 0005); the caller passes it
/// directly until then.
/// </param>
public sealed record CarrierBookingAnswerRequest(
    string CarrierId,
    CarrierBookingAnswerOutcome Outcome,
    string? CarrierBookingReference = null,
    DateTimeOffset? PlannedCollectionTime = null,
    CarrierBookingRejectionReason? ReasonCode = null,
    string? ReasonText = null);
