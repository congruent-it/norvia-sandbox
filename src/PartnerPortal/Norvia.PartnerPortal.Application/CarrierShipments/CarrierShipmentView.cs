using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierShipments;

/// <summary>A Shipment a Carrier Persona confirmed and hasn't delivered, with the Tracking events it may send next.</summary>
/// <param name="NextProgress">The progress events that are valid next.</param>
/// <param name="Exceptions">The exception events on offer at this stage.</param>
public sealed record CarrierShipmentView(
    string CarrierBookingNumber,
    string ShipmentNumber,
    string? CarrierBookingReference,
    CarrierBookingDetails Details,
    ShipmentStage Stage,
    IReadOnlyList<TrackingEventType> NextProgress,
    IReadOnlyList<TrackingEventType> Exceptions)
{
    public static CarrierShipmentView From(CarrierBookingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(
            request.CarrierBookingNumber, request.ShipmentNumber, request.CarrierBookingReference, request.Details, request.Stage,
            TrackingRules.NextProgress(request.LastProgress), TrackingRules.Exceptions(request.LastProgress));
    }
}
