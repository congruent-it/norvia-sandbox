using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Application.CarrierShipments.List;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierShipments;

public sealed class ListCarrierShipmentsHandlerTests
{
    [Fact]
    public async Task Handle_ListsTheShipmentsOfThePersona_WithWhatMayBeSentNext()
    {
        var request = CarrierBookingRequest.Receive(
            Samples.VanDijkTransport.Id, "CB-0000001", "SH-0000042", "release-1", Samples.Now,
            new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Utrecht", "NLD", null, null), null, []));
        request.Answer(new CarrierAnswer.Confirmation("VDT-1"), "answer-1", Samples.Now);
        var repository = Substitute.For<IReadRepository<CarrierBookingRequest>>();
        repository.ListAsync(Arg.Is<CarrierShipmentsOfPersonaSpec>(spec => spec != null), Arg.Any<CancellationToken>()).Returns([request]);

        var shipments = await new ListCarrierShipmentsHandler(repository).Handle(new ListCarrierShipmentsQuery("van-dijk-transport"), CancellationToken.None);

        var shipment = shipments.Should().ContainSingle().Subject;
        shipment.CarrierBookingNumber.Should().Be("CB-0000001");
        shipment.ShipmentNumber.Should().Be("SH-0000042");
        shipment.CarrierBookingReference.Should().Be("VDT-1");
        shipment.Stage.Should().Be(ShipmentStage.Booked);
        shipment.NextProgress.Should().Equal(TrackingEventType.Collected, TrackingEventType.Delivered);
        shipment.Exceptions.Should().Equal(TrackingEventType.CollectionFailed, TrackingEventType.DeliveryFailed, TrackingEventType.Damaged);
    }
}
