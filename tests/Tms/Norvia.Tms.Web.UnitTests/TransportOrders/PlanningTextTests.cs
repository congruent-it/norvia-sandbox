using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class PlanningTextTests
{
    [Theory]
    [InlineData("3087 BM", "Rotterdam", "NL", "Rotterdam, NL")]
    [InlineData("3087 BM", null, "NL", "3087 BM, NL")]
    [InlineData(null, "Rotterdam", null, "Rotterdam")]
    [InlineData(null, " ", null, "Not given")]
    public void Place_APlace_NamesItsTownAndCountry(string? postcode, string? city, string? country, string expected)
    {
        PlanningText.Place(new PlaceView(postcode, city, country)).Should().Be(expected);
    }

    [Fact]
    public void Place_NoPlace_SaysItWasNotGiven()
    {
        PlanningText.Place(null).Should().Be("Not given");
    }

    [Fact]
    public void CarriedBy_OwnFleet_SaysOwnFleet()
    {
        PlanningText.CarriedBy(new CarriedByView(AssignedTo.OwnFleet, null)).Should().Be("Own fleet");
    }

    [Fact]
    public void CarriedBy_ACarrier_NamesIt()
    {
        PlanningText.CarriedBy(new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0002", "Kessler Spedition"))).Should().Be("Kessler Spedition");
    }

    [Fact]
    public void CarriedBy_ACarrierTheTmsDoesntKnow_GivesItsId()
    {
        PlanningText.CarriedBy(new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0099", null))).Should().Be("CAR-0099");
    }
}
