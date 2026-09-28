using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Carriers.ListActive;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Carriers;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Carriers;

public sealed class ActiveCarriersQueryTests
{
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();

    [Fact]
    public async Task Handle_ActiveCarriers_ListsEachInTheDirectorysOrder()
    {
        _carriers.ListActive().Returns(
        [
            new Carrier(new CarrierId("CAR-0002"), "Kessler Spedition", true),
            new Carrier(new CarrierId("CAR-0003"), "PakketPunt Express", true),
            new Carrier(new CarrierId("CAR-0001"), "Van Dijk Transport", true),
        ]);

        var carriers = await new ListActiveCarriersHandler(_carriers).Handle(new ListActiveCarriersQuery(), CancellationToken.None);

        carriers.Should().Equal(
            new CarrierView("CAR-0002", "Kessler Spedition"),
            new CarrierView("CAR-0003", "PakketPunt Express"),
            new CarrierView("CAR-0001", "Van Dijk Transport"));
    }

    [Fact]
    public async Task Handle_NoActiveCarriers_ListsNothing()
    {
        _carriers.ListActive().Returns([]);

        var carriers = await new ListActiveCarriersHandler(_carriers).Handle(new ListActiveCarriersQuery(), CancellationToken.None);

        carriers.Should().BeEmpty();
    }
}
