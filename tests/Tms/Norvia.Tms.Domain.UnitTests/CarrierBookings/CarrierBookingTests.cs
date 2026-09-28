using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.CarrierBookings;

/// <summary>
/// The TMS creates a Carrier booking at release (P4), Requested, waiting for the Carrier's answer. This slice covers
/// only that creation: the Carrier's answer, sent/not-delivered reporting and withdrawal are later tickets.
/// </summary>
public sealed class CarrierBookingTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly CorrelationId Release = new("corr-release");
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 24, 12, 45, 0, TimeSpan.Zero);
    private static readonly ShipmentId Shipment = ShipmentId.New();
    private static readonly ShipmentNumber ShipmentNumberOfTwelve = ShipmentNumber.FromSequence(12);

    [Fact]
    public void Request_ForACarrierShipmentsRelease_IsRequested_WithTheShipmentAndTheCarrier()
    {
        var booking = CarrierBooking.Request(CarrierBookingNumber.FromSequence(42), Shipment, ShipmentNumberOfTwelve, VanDijk, Release, RequestedAt);

        booking.Number.Should().Be(CarrierBookingNumber.FromSequence(42));
        booking.ShipmentId.Should().Be(Shipment);
        booking.ShipmentNumber.Should().Be(ShipmentNumberOfTwelve);
        booking.CarrierId.Should().Be(VanDijk);
        booking.Status.Should().Be(CarrierBookingStatus.Requested);
        booking.CorrelationId.Should().Be(Release);
        booking.RequestedAt.Should().Be(RequestedAt);
    }

    [Fact]
    public void Request_RaisesNoDomainEvent_AsCreationIsntAnnouncedSeparatelyFromTheRelease()
    {
        var booking = CarrierBooking.Request(CarrierBookingNumber.FromSequence(42), Shipment, ShipmentNumberOfTwelve, VanDijk, Release, RequestedAt);

        booking.DomainEvents.Should().BeEmpty();
    }
}
