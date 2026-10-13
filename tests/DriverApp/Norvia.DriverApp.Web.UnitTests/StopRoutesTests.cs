using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Web.Reports;

namespace Norvia.DriverApp.Web.UnitTests;

/// <summary>The addresses of a Stop's screen and of the posts of what the Driver does there (P12).</summary>
public sealed class StopRoutesTests
{
    [Fact]
    public void TheScreen_IsOfTheTripTheShipmentAndTheKindOfStop() =>
        StopRoutes.Screen("TR-0000012", "SH-0000001", StopKindDto.Delivery).Should().Be("/trips/TR-0000012/stops/SH-0000001/delivery");

    [Theory]
    [InlineData(StopActionDto.Collected, "collected")]
    [InlineData(StopActionDto.CollectionFailed, "collection-failed")]
    [InlineData(StopActionDto.DeliveryFailed, "delivery-failed")]
    [InlineData(StopActionDto.Damaged, "damaged")]
    public void EachAction_HasAPostOfItsOwn_ThatReadsBackAsTheSameAction(StopActionDto action, string segment)
    {
        StopRoutes.Post("TR-0000012", "SH-0000001", StopKindDto.Collection, action).Should().Be($"/trips/TR-0000012/stops/SH-0000001/collection/{segment}");
        StopRoutes.TryParse(segment, out StopActionDto parsed).Should().BeTrue();
        parsed.Should().Be(action);
    }

    [Theory]
    [InlineData("collection", true, StopKindDto.Collection)]
    [InlineData("delivery", true, StopKindDto.Delivery)]
    [InlineData("pickup", false, StopKindDto.Delivery)]
    public void AKindOfStop_IsReadFromItsSegment_OrRefused(string segment, bool known, StopKindDto kind)
    {
        StopRoutes.TryParse(segment, out StopKindDto parsed).Should().Be(known);
        parsed.Should().Be(kind);
    }

    [Fact]
    public void AnUnknownAction_IsRefused() => StopRoutes.TryParse("vanished", out StopActionDto _).Should().BeFalse();

    [Fact]
    public void ANumberWithCharactersAUriNeedsEscaped_IsEscaped() =>
        StopRoutes.Screen("TR 1", "SH/1", StopKindDto.Collection).Should().Be("/trips/TR%201/stops/SH%2F1/collection");
}
