using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Erp.Application.CarrierCosts.ListOpen;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Application.UnitTests.CarrierCosts;

public sealed class ListOpenCarrierCostsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly IReadRepository<CarrierCost> _costs = Substitute.For<IReadRepository<CarrierCost>>();
    private readonly IReadRepository<Carrier> _carriers = Substitute.For<IReadRepository<Carrier>>();

    private static CarrierCost Cost(string number, string carrierId, decimal amount, CarrierCostSource? source = null) =>
        CarrierCost.Record(
            new CarrierCostData(
                number,
                "REF-" + number,
                new CarrierId(carrierId),
                "SH-0000001",
                "TO-0000001",
                new CarrierCostCharge(amount, [new ChargeLine(ChargeLineType.Freight, amount)]),
                source ?? CarrierCostSource.FromRateCard("CRC-0001", 1),
                Now,
                "delivered"),
            Now).Cost!;

    [Fact]
    public async Task ListOpen_GroupsTheOpenCostsPerCarrier_ByCarrierName_KeepingTheOrderOfTheSpecification()
    {
        _costs.ListAsync(Arg.Any<ISpecification<CarrierCost>>(), Arg.Any<CancellationToken>())
            .Returns([Cost("CB-0000001", "CAR-0002", 10.25m), Cost("CB-0000002", "CAR-0001", 5m, CarrierCostSource.FromSpot("Ferry strike")), Cost("CB-0000003", "CAR-0002", 20m)]);
        _carriers.ListAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>())
            .Returns([new Carrier(new CarrierId("CAR-0001"), "Zeeland Vervoer", true), new Carrier(new CarrierId("CAR-0002"), "Van Dijk Transport", true)]);

        var views = await new ListOpenCarrierCostsHandler(_costs, _carriers).Handle(new ListOpenCarrierCostsQuery(), CancellationToken.None);

        views.Select(view => (view.CarrierId, view.CarrierName, view.Total)).Should().Equal(("CAR-0002", "Van Dijk Transport", 30.25m), ("CAR-0001", "Zeeland Vervoer", 5m));
        views[0].Costs.Select(cost => cost.CarrierBookingNumber).Should().Equal("CB-0000001", "CB-0000003");
        views[1].Costs.Single().Source.Should().Be(new Norvia.Erp.Application.CarrierCosts.CarrierCostSourceView("Spot", null, null, "Ferry strike"));
    }

    [Fact]
    public async Task ListOpen_ForACarrierTheErpDoesntKnow_FallsBackToItsId()
    {
        _costs.ListAsync(Arg.Any<ISpecification<CarrierCost>>(), Arg.Any<CancellationToken>()).Returns([Cost("CB-0000001", "CAR-0009", 10m)]);
        _carriers.ListAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>()).Returns([]);

        var views = await new ListOpenCarrierCostsHandler(_costs, _carriers).Handle(new ListOpenCarrierCostsQuery(), CancellationToken.None);

        views.Single().CarrierName.Should().Be("CAR-0009");
    }

    [Fact]
    public async Task ListOpen_WithNoOpenCosts_IsEmpty()
    {
        _costs.ListAsync(Arg.Any<ISpecification<CarrierCost>>(), Arg.Any<CancellationToken>()).Returns([]);
        _carriers.ListAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>()).Returns([]);

        (await new ListOpenCarrierCostsHandler(_costs, _carriers).Handle(new ListOpenCarrierCostsQuery(), CancellationToken.None)).Should().BeEmpty();
    }
}
