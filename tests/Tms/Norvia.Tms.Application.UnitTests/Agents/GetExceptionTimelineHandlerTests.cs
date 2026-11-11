using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.UnitTests.Exceptions;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TrackingEvents;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Agents;

public sealed class GetExceptionTimelineHandlerTests
{
    private readonly IReadRepository<ExceptionCase> _exceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _events = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ShipperNotification> _notifications = Substitute.For<IReadRepository<ShipperNotification>>();
    private readonly IReadRepository<CarrierBooking> _bookings = Substitute.For<IReadRepository<CarrierBooking>>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();

    private GetExceptionTimelineHandler Handler => new(_exceptions, _shipments, _events, _notifications, _bookings, _carriers);

    public GetExceptionTimelineHandlerTests()
    {
        _carriers.Find(new CarrierId("CAR-0001")).Returns(new Carrier(new CarrierId("CAR-0001"), "Van Dijk Transport", true));
        _events.ListAsync(Arg.Any<TrackingTimelineSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _notifications.ListAsync(Arg.Any<ShipperNotificationsOfExceptionsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _bookings.ListAsync(Arg.Any<CarrierBookingsByShipmentSpec>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public async Task Handle_AnException_IsItsTimelineWithTheShipmentsStatusAndWhoCarriesIt()
    {
        var shipment = OpenExceptions.BookedShipment();
        var exception = OpenExceptions.DeliveryFailedOn(shipment);
        _exceptions.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(exception);
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);

        var result = await Handler.Handle(new GetExceptionTimelineQuery("EX-0000005"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ShipmentNumber.Should().Be(shipment.Number.Value);
        result.Value.ShipmentStatus.Should().Be(shipment.Status);
        result.Value.CarriedBy.Carrier!.Name.Should().Be("Van Dijk Transport");
        result.Value.Exception.ExceptionNumber.Should().Be("EX-0000005");
    }

    [Theory]
    [InlineData("not-a-number", false, false)]
    [InlineData("EX-0000005", true, false)]
    [InlineData("EX-0000005", false, true)]
    public async Task Handle_NoSuchExceptionOrShipment_IsNotFound(string number, bool exceptionMissing, bool shipmentMissing)
    {
        var shipment = OpenExceptions.BookedShipment();
        _exceptions.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(exceptionMissing ? null : OpenExceptions.DeliveryFailedOn(shipment));
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipmentMissing ? null : shipment);

        var result = await Handler.Handle(new GetExceptionTimelineQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }
}
