using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierBookings;

/// <summary>A booking request in the Carrier's inbox, with what it asks.</summary>
public sealed record CarrierBookingRequestView(
    Guid Id,
    string CarrierBookingNumber,
    string ShipmentNumber,
    DateTimeOffset ReceivedAt,
    CarrierBookingDetails Details)
{
    public static CarrierBookingRequestView From(CarrierBookingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(request.Id.Value, request.CarrierBookingNumber, request.ShipmentNumber, request.ReceivedAt, request.Details);
    }
}
