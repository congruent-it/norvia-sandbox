using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierBookings;

/// <summary>A booking request in the Carrier's inbox, with what it asks and, once it is closed, how.</summary>
/// <param name="ClosedAt">When the Carrier answered, or Norvia was found to have withdrawn the request; null while Open.</param>
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
    string? RejectionText = null)
{
    public bool IsOpen => Status == CarrierBookingRequestStatus.Open;

    public static CarrierBookingRequestView From(CarrierBookingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(
            request.Id.Value, request.CarrierBookingNumber, request.ShipmentNumber, request.ReceivedAt, request.Details,
            request.Status, request.ClosedAt, request.AnswerCorrelationId, request.CarrierBookingReference, request.PlannedCollection,
            request.RejectionReason, request.RejectionText);
    }
}
