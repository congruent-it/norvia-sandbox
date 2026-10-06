using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierShipments;

/// <summary>The outbound Partner API client replaced by a fake that keeps every Tracking event and Proof of delivery it was given and answers as set.</summary>
internal sealed class RecordingPartnerApi : INorviaPartnerApi
{
    public List<(Persona Persona, string ShipmentNumber, CarrierTrackingEvent Event)> TrackingEvents { get; } = [];

    public List<(Persona Persona, string ShipmentNumber, CarrierProofOfDelivery Proof)> ProofsOfDelivery { get; } = [];

    public TrackingReply Reply { get; set; } = TrackingReply.Accepted("corr-1");

    public Task<TrackingReply> SubmitTrackingAsync(Persona persona, string shipmentNumber, CarrierTrackingEvent trackingEvent, CancellationToken cancellationToken)
    {
        TrackingEvents.Add((persona, shipmentNumber, trackingEvent));
        return Task.FromResult(Reply);
    }

    public Task<TrackingReply> SubmitProofOfDeliveryAsync(Persona persona, string shipmentNumber, CarrierProofOfDelivery proof, CancellationToken cancellationToken)
    {
        ProofsOfDelivery.Add((persona, shipmentNumber, proof));
        return Task.FromResult(Reply);
    }

    public Task<NorviaAnswer> SubmitAsync(Persona persona, BookingRequest booking, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<NorviaReply> AnswerAsync(Persona persona, string carrierBookingNumber, string shipmentNumber, CarrierAnswer answer, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
