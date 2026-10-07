using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierBookings;

/// <summary>A booking request in the Carrier's inbox, with what it asks and, once it is closed, how.</summary>
/// <param name="ClosedAt">When the Carrier answered, or Norvia was found to have withdrawn the request; null while Open.</param>
/// <param name="CancellationText">Why Norvia cancelled the confirmed booking; null unless it did.</param>
/// <param name="AutoAnswerMode">The automatic answer the request was received with; null for one answered by hand.</param>
/// <param name="AutoAnswerDueAt">When that answer is due; null once the request is closed.</param>
/// <param name="AnswerCorrelationId">The correlation ID Norvia gave the answer, by which Norvia finds every trace of it.</param>
public sealed record CarrierBookingRequestView(
    Guid Id,
    string CarrierBookingNumber,
    string ShipmentNumber,
    DateTimeOffset ReceivedAt,
    CarrierBookingDetails Details,
    CarrierBookingRequestStatus Status = CarrierBookingRequestStatus.Open,
    DateTimeOffset? ClosedAt = null,
    string? AnswerCorrelationId = null,
    string? CarrierBookingReference = null,
    DateTimeOffset? PlannedCollection = null,
    CarrierRejectionReason? RejectionReason = null,
    string? RejectionText = null,
    CarrierAnswerMode? AutoAnswerMode = null,
    DateTimeOffset? AutoAnswerDueAt = null,
    string? CancellationText = null)
{
    public bool IsOpen => Status == CarrierBookingRequestStatus.Open;

    public static CarrierBookingRequestView From(CarrierBookingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(
            request.Id.Value, request.CarrierBookingNumber, request.ShipmentNumber, request.ReceivedAt, request.Details,
            request.Status, request.ClosedAt, request.AnswerCorrelationId, request.CarrierBookingReference, request.PlannedCollection,
            request.RejectionReason, request.RejectionText, request.AutoAnswerMode, request.AutoAnswerDueAt, request.CancellationText);
    }
}
