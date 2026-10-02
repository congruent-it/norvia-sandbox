using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Application.CarrierBookings.List;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierBookings;

public sealed class ListCarrierBookingRequestsHandlerTests
{
    [Fact]
    public async Task Handle_ReadsThePersonasInbox_WithWhatEachRequestAsks()
    {
        var details = new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Utrecht", "NLD", null, null), "Café De Zwaan", []);
        var request = CarrierBookingRequest.Receive(Samples.VanDijkTransport.Id, "CB-0000001", "SH-0000042", "release-1", Samples.Now, details);
        var repository = Substitute.For<IReadRepository<CarrierBookingRequest>>();
        repository.ListAsync(Arg.Is<CarrierBookingRequestsOfPersonaSpec>(spec => spec.PersonaId == Samples.VanDijkTransport.Id), Arg.Any<CancellationToken>())
            .Returns([request]);

        var inbox = await new ListCarrierBookingRequestsHandler(repository).Handle(new ListCarrierBookingRequestsQuery("van-dijk-transport"), CancellationToken.None);

        var view = inbox.Should().ContainSingle().Subject;
        view.Id.Should().Be(request.Id.Value);
        view.CarrierBookingNumber.Should().Be("CB-0000001");
        view.ShipmentNumber.Should().Be("SH-0000042");
        view.ReceivedAt.Should().Be(Samples.Now);
        view.Details.Should().Be(details);
    }
}
