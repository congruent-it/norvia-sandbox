using System.Text.Json.Nodes;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

public sealed class PartnerBookingFormatTests
{
    // 09:30 in Rotterdam (UTC+2 until 25 October 2026).
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 7, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ToJson_EveryGoodBooking_KeepsToThePartnerBookingSchema()
    {
        foreach (var booking in TestPlans.Plan(orders: 60).Bookings)
        {
            Contracts.BookingSchemaErrors(PartnerBookingFormat.ToJson(booking, Now)).Should().BeEmpty($"booking {booking.Reference} is a good one");
        }
    }

    [Fact]
    public void ToJson_GoodBooking_NamesTheReferenceBothAddressesAndTheGoods()
    {
        var booking = TestPlans.Good();

        var json = PartnerBookingFormat.ToJson(booking, Now);

        json["bookingRef"]!.GetValue<string>().Should().Be("SIM-7-1");
        json["pickupCity"]!.GetValue<string>().Should().Be(booking.Pickup.City);
        json["receiverPostalCode"]!.GetValue<string>().Should().Be(booking.Delivery.Postcode);
        json["receiverName"]!.GetValue<string>().Should().Be(booking.ConsigneeName);
        json["items"]!.AsArray().Should().HaveCount(booking.Goods.Count);
    }

    [Fact]
    public void ToJson_TheCollectionWindow_IsLocalTimeInTheFormatOfThePartnerApi()
    {
        var booking = TestPlans.Good() with { Slot = new PickupSlot(DayOffset: 2, FromHour: 8, Hours: 3) };

        var json = PartnerBookingFormat.ToJson(booking, Now);

        json["pickupFrom"]!.GetValue<string>().Should().Be("15-10-2026 08:00");
        json["pickupUntil"]!.GetValue<string>().Should().Be("15-10-2026 11:00");
    }

    [Fact]
    public void ToJson_AWeightWithDecimals_IsWrittenWithAPointAndAUnit()
    {
        var booking = TestPlans.Good() with { Goods = [new PlannedGoodsLine(PackageKind.Crate, 2, 12.5m, 40, 30, 20)] };

        var item = PartnerBookingFormat.ToJson(booking, Now)["items"]![0]!;

        item["weight"]!.GetValue<string>().Should().Be("12.5 kg");
        item["dimensions"]!.GetValue<string>().Should().Be("40x30x20 cm");
        item["packaging"]!.GetValue<string>().Should().Be("CR");
    }

    [Theory]
    [InlineData(PackageKind.Pallet, "PX")]
    [InlineData(PackageKind.Parcel, "PK")]
    [InlineData(PackageKind.Crate, "CR")]
    [InlineData(PackageKind.Loose, "NE")]
    public void ToJson_PackageKinds_AreUnEceRecommendation21Codes(PackageKind kind, string code)
    {
        var booking = TestPlans.Good() with { Goods = [new PlannedGoodsLine(kind, 1, 10, null, null, null)] };

        var item = PartnerBookingFormat.ToJson(booking, Now)["items"]![0]!;

        item["packaging"]!.GetValue<string>().Should().Be(code);
        item.AsObject().ContainsKey("dimensions").Should().BeFalse();
    }

    [Fact]
    public void ToJson_InvalidContentWithoutAConsignee_LeavesTheNameOutSoNorviaNamesIt()
    {
        var booking = TestPlans.Good() with { Bad = BadBookingKind.InvalidContent, ConsigneeName = null };

        PartnerBookingFormat.ToJson(booking, Now).ContainsKey("receiverName").Should().BeFalse();
    }

    [Fact]
    public void ToJson_InvalidContentWithoutGoods_LeavesTheItemsOut()
    {
        var booking = TestPlans.Good() with { Bad = BadBookingKind.InvalidContent, Goods = [] };

        PartnerBookingFormat.ToJson(booking, Now).ContainsKey("items").Should().BeFalse();
    }

    [Fact]
    public void ToJson_InvalidContentWithZeroWeight_SendsTheZero()
    {
        var booking = TestPlans.Good() with { Goods = [new PlannedGoodsLine(PackageKind.Pallet, 1, 0, null, null, null)] };

        PartnerBookingFormat.ToJson(booking, Now)["items"]![0]!["weight"]!.GetValue<string>().Should().Be("0 kg");
    }

    [Fact]
    public void ToJson_NoLaneBooking_DeliversToGermany()
    {
        var json = PartnerBookingFormat.ToJson(TestPlans.Bad(BadBookingKind.NoLane), Now);

        json["receiverCountry"]!.GetValue<string>().Should().Be("DE");
        Contracts.BookingSchemaErrors(json).Should().BeEmpty("Norvia accepts the booking and Rating then finds no Lane");
    }

    [Fact]
    public void ToJson_ARepeatedBooking_IsTheOriginalOnceMore()
    {
        var plan = TestPlans.Plan(orders: 200, bad: 60, seed: 9);
        var repeat = plan.Bookings.First(booking => booking.Bad == BadBookingKind.RepeatedReference);
        var original = plan.Bookings.First(booking => booking.Reference == repeat.Reference && booking.Bad is null);

        JsonNode.DeepEquals(PartnerBookingFormat.ToJson(repeat, Now), PartnerBookingFormat.ToJson(original, Now)).Should().BeTrue();
    }
}
