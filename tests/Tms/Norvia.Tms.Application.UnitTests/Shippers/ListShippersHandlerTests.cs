using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.Shippers.List;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Shippers;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shippers;

public sealed class ListShippersHandlerTests
{
    [Fact]
    public async Task Handle_ListsTheShippersByName()
    {
        var shippers = Substitute.For<IShipperDirectory>();
        shippers.List().Returns(
        [
            new Shipper(new ShipperId("SHP-0002"), "Verhoeven Foods", true),
            new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true),
        ]);

        var listed = await new ListShippersHandler(shippers).Handle(new ListShippersQuery(), CancellationToken.None);

        listed.Should().Equal(new ShipperView("SHP-0001", "Brouwer Dranken"), new ShipperView("SHP-0002", "Verhoeven Foods"));
    }
}
