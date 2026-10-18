using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Erp.Application.MasterData.Carriers.List;
using Norvia.Erp.Application.MasterData.RateCards;
using Norvia.Erp.Application.MasterData.RateCards.List;
using Norvia.Erp.Application.MasterData.RateCards.ListCarrier;
using Norvia.Erp.Application.MasterData.Shippers.List;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.RateCards;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.MasterData;

public sealed class ListMasterDataHandlerTests
{
    private static readonly RateCardVersion Version = new(
        1,
        new DateOnly(2026, 1, 1),
        null,
        [new Zone("Rotterdam", "NL", ["30"]), new Zone("Utrecht", "NL", ["34"])],
        [new Lane("Rotterdam", "Utrecht", 65m, [new WeightBreak(500, 11.8m), new WeightBreak(1000, 10.2m)])]);

    [Fact]
    public async Task ListShippers_ReturnsThemAsViews()
    {
        var shippers = Substitute.For<IReadRepository<Shipper>>();
        shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns([new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true), new Shipper(new ShipperId("SHP-0002"), "Old", false)]);

        var views = await new ListShippersHandler(shippers).Handle(new ListShippersQuery(), CancellationToken.None);

        views.Should().Equal(new ShipperView("SHP-0001", "Brouwer Dranken", true), new ShipperView("SHP-0002", "Old", false));
    }

    [Fact]
    public async Task ListCarriers_ReturnsThemAsViews()
    {
        var carriers = Substitute.For<IReadRepository<Carrier>>();
        carriers.ListAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>())
            .Returns([new Carrier(new CarrierId("CAR-0001"), "Van Dijk Transport", true)]);

        var views = await new ListCarriersHandler(carriers).Handle(new ListCarriersQuery(), CancellationToken.None);

        views.Should().Equal(new CarrierView("CAR-0001", "Van Dijk Transport", true));
    }

    [Fact]
    public async Task ListRateCards_NamesTheShipperAndSummarisesVersionsAndLanes()
    {
        var rateCards = Substitute.For<IReadRepository<RateCard>>();
        rateCards.ListAsync(Arg.Any<ISpecification<RateCard>>(), Arg.Any<CancellationToken>()).Returns([
            new RateCard(new RateCardId("RC-0002"), new ShipperId("SHP-0003"), [Version]),
            new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [Version with { Version = 2 }, Version]),
        ]);
        var shippers = Substitute.For<IReadRepository<Shipper>>();
        shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns([new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true)]);

        var views = await new ListRateCardsHandler(rateCards, shippers).Handle(new ListRateCardsQuery(), CancellationToken.None);

        views.Select(view => view.Id).Should().Equal("RC-0001", "RC-0002");
        views[0].PartyName.Should().Be("Brouwer Dranken");
        views[1].PartyName.Should().Be("SHP-0003", "a Shipper the ERP doesn't know is shown by its ID");
        views[0].Versions.Select(version => version.Version).Should().Equal(1, 2);
        views[0].Versions[0].ZoneCount.Should().Be(2);
        views[0].Versions[0].Lanes.Should().Equal(new LaneView("Rotterdam", "Utrecht", 65m, 2));
    }

    [Fact]
    public async Task ListCarrierRateCards_NamesTheCarrier()
    {
        var rateCards = Substitute.For<IReadRepository<CarrierRateCard>>();
        rateCards.ListAsync(Arg.Any<ISpecification<CarrierRateCard>>(), Arg.Any<CancellationToken>())
            .Returns([new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), [Version]), new CarrierRateCard(new RateCardId("CRC-0000"), new CarrierId("CAR-0009"), [])]);
        var carriers = Substitute.For<IReadRepository<Carrier>>();
        carriers.ListAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>())
            .Returns([new Carrier(new CarrierId("CAR-0001"), "Van Dijk Transport", true)]);

        var views = await new ListCarrierRateCardsHandler(rateCards, carriers).Handle(new ListCarrierRateCardsQuery(), CancellationToken.None);

        views.Select(view => view.Id).Should().Equal("CRC-0000", "CRC-0001");
        views[0].PartyName.Should().Be("CAR-0009");
        views[1].PartyName.Should().Be("Van Dijk Transport");
        views[1].Versions.Should().ContainSingle().Which.Lanes.Should().ContainSingle();
    }

    [Fact]
    public async Task ListShippers_WithoutAQuery_Throws()
    {
        var handling = async () => await new ListShippersHandler(Substitute.For<IReadRepository<Shipper>>()).Handle(null!, CancellationToken.None);

        await handling.Should().ThrowAsync<ArgumentNullException>();
    }
}
