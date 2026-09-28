using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.GetByNumber;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class ShipmentQueryTests
{
    private readonly IReadRepository<Shipment> _repository = Substitute.For<IReadRepository<Shipment>>();
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly GetShipmentHandler _handler;

    public ShipmentQueryTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _carriers.Find(new CarrierId("CAR-0001")).Returns(new Carrier(new CarrierId("CAR-0001"), "Van Dijk Transport", true));
        _handler = new GetShipmentHandler(_repository, _shippers, _carriers);
    }

    [Fact]
    public async Task Get_ShipmentPlannedWithACarrier_ShowsItsStatusAndTheCarrierThatCarriesIt()
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(sequence: 7), CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12, correlationId: "corr-planning");
        _repository.FirstOrDefaultAsync(Arg.Is<ShipmentByNumberSpec>(s => s.Number.Value == "SH-0000012"), Arg.Any<CancellationToken>())
            .Returns(shipment);

        var result = await _handler.Handle(new GetShipmentQuery("SH-0000012"), CancellationToken.None);

        result.Value.Should().BeEquivalentTo(new ShipmentView(
            "SH-0000012",
            ShipmentStatus.Planned,
            "TO-0000007",
            new ShipperView("SHP-0001", "Brouwer Dranken"),
            new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
            "corr-planning",
            Samples.Now.AddHours(2),
            [new ShipmentAssignmentView(new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")), "corr-planning", Samples.Now.AddHours(2))]));
    }

    [Fact]
    public async Task Get_ReassignedShipment_ShowsTheHistoryOfAssignmentsInOrder()
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(), CarriedBy.Carrier(new CarrierId("CAR-0001")), correlationId: "corr-planning");
        shipment.Reassign(CarriedBy.OwnFleet, new("key-r1"), new("corr-reassign"), Samples.Now.AddHours(3));
        _repository.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);

        var result = await _handler.Handle(new GetShipmentQuery("SH-0000003"), CancellationToken.None);

        result.Value.CarriedBy.Should().Be(new CarriedByView(AssignedTo.OwnFleet, null));
        result.Value.Assignments.Should().Equal(
            new ShipmentAssignmentView(new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")), "corr-planning", Samples.Now.AddHours(2)),
            new ShipmentAssignmentView(new CarriedByView(AssignedTo.OwnFleet, null), "corr-reassign", Samples.Now.AddHours(3)));
    }

    [Fact]
    public async Task Get_OwnFleetShipment_ShowsOwnFleet()
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(), CarriedBy.OwnFleet);
        _repository.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);

        var result = await _handler.Handle(new GetShipmentQuery("SH-0000003"), CancellationToken.None);

        result.Value.CarriedBy.Should().Be(new CarriedByView(AssignedTo.OwnFleet, null));
    }

    [Fact]
    public async Task Get_ShipmentWhoseCarrierIsNoLongerInTheCarrierData_StillShowsTheCarriersId()
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(), CarriedBy.Carrier(new CarrierId("CAR-0404")));
        _repository.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);

        var result = await _handler.Handle(new GetShipmentQuery("SH-0000003"), CancellationToken.None);

        result.Value.CarriedBy.Carrier.Should().Be(new CarrierView("CAR-0404", null));
    }

    [Theory]
    [InlineData("SH-0000099")]
    [InlineData("TO-0000007")]
    public async Task Get_UnknownOrMalformedNumber_ReturnsNotFound(string number)
    {
        var result = await _handler.Handle(new GetShipmentQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }
}
