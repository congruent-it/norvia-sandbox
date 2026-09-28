using Ardalis.SharedKernel;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class ShipmentsToReleaseQueryTests
{
    private readonly IReadRepository<Shipment> _repository = Substitute.For<IReadRepository<Shipment>>();
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly ListShipmentsToReleaseHandler _handler;

    public ShipmentsToReleaseQueryTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _carriers.Find(new CarrierId("CAR-0001")).Returns(new Carrier(new CarrierId("CAR-0001"), "Van Dijk Transport", true));
        _handler = new ListShipmentsToReleaseHandler(_repository, _shippers, _carriers);
    }

    [Fact]
    public async Task Handle_PlannedShipments_ListsEachWithWhoCarriesIt()
    {
        var carrier = Samples.Planned(Samples.AcceptedOrder(sequence: 7), CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        var ownFleet = Samples.Planned(Samples.AcceptedOrder(sequence: 8), CarriedBy.OwnFleet, sequence: 13);
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([carrier, ownFleet]);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().Equal(
            new ShipmentToReleaseSummary(
                "SH-0000012",
                "TO-0000007",
                new ShipperView("SHP-0001", "Brouwer Dranken"),
                new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
                Samples.Now.AddHours(2)),
            new ShipmentToReleaseSummary(
                "SH-0000013",
                "TO-0000008",
                new ShipperView("SHP-0001", "Brouwer Dranken"),
                new CarriedByView(AssignedTo.OwnFleet, null),
                Samples.Now.AddHours(2)));
    }

    [Fact]
    public async Task Handle_NoPlannedShipments_ListsNothing()
    {
        _repository.ListAsync(Arg.Any<ShipmentsToReleaseSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        var shipments = await _handler.Handle(new ListShipmentsToReleaseQuery(), CancellationToken.None);

        shipments.Should().BeEmpty();
    }
}
