using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Erp.Application.MasterData.Carriers.List;
using Norvia.Erp.Application.MasterData.RateCards;
using Norvia.Erp.Application.MasterData.RateCards.List;
using Norvia.Erp.Application.MasterData.RateCards.ListCarrier;
using Norvia.Erp.Application.MasterData.Shippers.List;
using Norvia.Erp.Web.MasterData;
using Norvia.Erp.Web.Operators;

namespace Norvia.Erp.Web.UnitTests.MasterData;

public sealed class MasterDataPagesTests : ErpWebContext
{
    private static readonly RateCardView Card = new(
        "RC-0001",
        "SHP-0001",
        "Brouwer Dranken",
        [new RateCardVersionView(1, new DateOnly(2026, 1, 1), null, 6, [new LaneView("Rotterdam", "Utrecht", 65m, 4)])]);

    [Fact]
    public void RenderShippers_ListsThemWithALinkToCreateAndEachOne()
    {
        Answer<ListShippersQuery, IReadOnlyList<ShipperView>>([new ShipperView("SHP-0001", "Brouwer Dranken", true), new ShipperView("SHP-0002", "Old Shipper", false)]);

        var page = Render<ShippersPage>();

        page.Markup.Should().Contain("SHP-0001").And.Contain("Brouwer Dranken").And.Contain("Old Shipper");
        page.FindAll("td").Select(cell => cell.TextContent.Trim()).Should().Contain(["Yes", "No"]);
        page.FindAll("button, form, input").Should().BeEmpty("a record is edited on a page of its own");
        page.FindAll("a").Select(link => link.GetAttribute("href")).Should().Contain(["/shippers/new", "/shippers/SHP-0001/edit", "/shippers/SHP-0002/edit"]);
    }

    [Fact]
    public void RenderCarriers_ListsThemWithALinkToCreateAndEachOne()
    {
        Answer<ListCarriersQuery, IReadOnlyList<CarrierView>>([new CarrierView("CAR-0001", "Van Dijk Transport", true)]);

        var page = Render<CarriersPage>();

        page.Markup.Should().Contain("CAR-0001").And.Contain("Van Dijk Transport");
        page.FindAll("button, form, input").Should().BeEmpty();
        page.FindAll("a").Select(link => link.GetAttribute("href")).Should().Contain(["/carriers/new", "/carriers/CAR-0001/edit"]);
    }

    [Fact]
    public void RenderRateCards_ShowsEachCardWithItsShipperVersionsAndLanes()
    {
        Answer<ListRateCardsQuery, IReadOnlyList<RateCardView>>([Card]);

        var page = Render<RateCardsPage>();

        page.Find("section").TextContent.Should().Contain("RC-0001").And.Contain("Shipper Brouwer Dranken (SHP-0001)")
            .And.Contain("Version 1").And.Contain("with no end date").And.Contain("Rotterdam").And.Contain("Utrecht").And.Contain("65.00");
        page.FindAll("button, form, input").Should().BeEmpty();
        page.FindAll("a").Select(link => link.GetAttribute("href")).Should().Contain(["/rate-cards/new", "/rate-cards/RC-0001/edit"]);
    }

    [Fact]
    public void RenderCarrierRateCards_ShowsEachCardWithItsCarrier()
    {
        var card = Card with { Id = "CRC-0001", PartyId = "CAR-0001", PartyName = "Van Dijk Transport" };
        Answer<ListCarrierRateCardsQuery, IReadOnlyList<RateCardView>>([card]);

        var page = Render<CarrierRateCardsPage>();

        page.Find("section").TextContent.Should().Contain("CRC-0001").And.Contain("Carrier Van Dijk Transport (CAR-0001)");
    }

    [Fact]
    public void RenderRateCards_WithAnEndDate_ShowsTheLastValidDay()
    {
        var ended = Card with { Versions = [Card.Versions[0] with { ValidTo = new DateOnly(2026, 12, 31) }] };
        Answer<ListRateCardsQuery, IReadOnlyList<RateCardView>>([ended]);

        Render<RateCardsPage>().Markup.Should().Contain("to 2026-12-31");
    }

    [Fact]
    public void RenderShippers_WithNone_SaysThereAreNone()
    {
        Answer<ListShippersQuery, IReadOnlyList<ShipperView>>([]);

        Render<ShippersPage>().Markup.Should().Contain("There are no shippers yet");
    }

    [Fact]
    public void RenderShippers_WhenTheyCannotBeRead_SaysSoWithoutDetailsOfTheFailure()
    {
        Fail<ListShippersQuery, IReadOnlyList<ShipperView>>(new InvalidOperationException("db password wrong"));

        var page = Render<ShippersPage>();

        page.Markup.Should().Contain("couldn't be read").And.NotContain("db password");
    }

    [Fact]
    public void RenderStates_BeforeTheListHasLoaded_ShowsProgress()
    {
        var page = Render<MasterDataStates>(parameters => parameters.Add(states => states.Name, "Shippers"));

        page.Markup.Should().Contain("Loading the Shippers");
    }

    [Theory]
    [InlineData(typeof(ShippersPage))]
    [InlineData(typeof(CarriersPage))]
    [InlineData(typeof(RateCardsPage))]
    [InlineData(typeof(CarrierRateCardsPage))]
    public void EveryMasterDataPage_RequiresTheFinanceRole(Type page)
    {
        var policies = page.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>().Select(attribute => attribute.Policy);

        policies.Should().Contain(FinanceSignIn.FinancePolicy);
    }
}
