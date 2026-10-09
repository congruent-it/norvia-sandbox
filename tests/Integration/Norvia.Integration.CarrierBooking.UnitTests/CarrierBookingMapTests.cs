using System.Text.Json.Nodes;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public class CarrierBookingMapTests
{
    // 2030-10-02 is summer time in the Netherlands and Germany (UTC+2); 2030-01-15 is winter time (UTC+1).
    private const string RateCardCost = """
        {
          "currency": "EUR", "total": 310.00,
          "chargeLines": [ { "type": "Freight", "amount": 290.00 }, { "type": "MinimumChargeAdjustment", "amount": 20.00 } ],
          "source": { "kind": "CarrierRateCard", "rateCardVersion": { "rateCardId": "RC-1", "version": 3 } },
          "calculatedAt": "2030-10-01T08:00:00+00:00"
        }
        """;

    private const string SpotCost = """
        {
          "currency": "EUR", "total": 480.00,
          "chargeLines": [ { "type": "Freight", "amount": 480.00 } ],
          "source": { "kind": "Spot", "remark": "agreed with Van Dijk by phone, 480" },
          "calculatedAt": "2030-10-01T08:00:00+00:00"
        }
        """;

    private static string Release(string collectionEarliest = "2030-10-02T05:30:00+00:00", string collectionLatest = "2030-10-02T09:00:00+00:00", string carrierCost = RateCardCost) => $$"""
        {
          "eventId": "5c2c7f0e-3a4c-4d0a-9f3e-5a7a1c0c9b11",
          "eventType": "ShipmentReleased",
          "eventVersion": 1,
          "occurredAt": "2030-10-01T08:00:00+00:00",
          "correlationId": "release-1",
          "shipmentNumber": "SH-0000042",
          "transportOrderNumber": "TO-0000007",
          "shipperId": "SHIPPER-ACME",
          "assignedTo": "Carrier",
          "carrierId": "CARRIER-VAN-DIJK",
          "carrierBookingNumber": "CB-0000001",
          "carrierCost": {{carrierCost}},
          "transportOrder": {
            "transportOrderNumber": "TO-0000007",
            "shipperId": "SHIPPER-ACME",
            "shipperReference": "ACME-REF-9",
            "freightCharge": { "total": 412.50, "currency": "EUR" },
            "collection": {
              "address": { "street": "Waalhaven Oostzijde 88", "postcode": "3087 BM", "city": "Rotterdam", "country": "NL" },
              "window": { "earliest": "{{collectionEarliest}}", "latest": "{{collectionLatest}}" }
            },
            "delivery": {
              "address": { "street": "Kölner Landstraße 301", "postcode": "40591", "city": "Düsseldorf", "country": "DE" },
              "consignee": { "name": "Getränke Schmitz GmbH", "contactName": "Jonas Schmitz", "phone": "+49 211 555 0190" },
              "window": { "earliest": "2030-10-03T06:00:00+00:00", "latest": "2030-10-03T14:30:00+00:00" }
            },
            "goodsLines": [
              { "packageType": "pallet", "quantity": 4, "weightKg": 2400, "dimensionsCm": { "length": 120, "width": 80, "height": 160 } },
              { "packageType": "parcel", "quantity": 3, "weightKg": 12 }
            ],
            "instructions": "Deliver at dock 3."
          }
        }
        """;

    private static JsonObject Map(string release) =>
        CarrierBookingMap.ToCarrierBooking(release) ?? throw new InvalidOperationException("The release didn't map.");

    [Fact]
    public void ToCarrierBooking_ACarrierRelease_CarriesTheBookingAndShipmentNumbers()
    {
        var booking = Map(Release());

        booking["carrierBookingNumber"]!.GetValue<string>().Should().Be("CB-0000001");
        booking["shipmentNumber"]!.GetValue<string>().Should().Be("SH-0000042");
    }

    [Fact]
    public void ToCarrierBooking_ARelease_FlattensTheAddressesAndNamesTheCountriesByAlpha3()
    {
        var booking = Map(Release());

        booking["collectionStreet"]!.GetValue<string>().Should().Be("Waalhaven Oostzijde 88");
        booking["collectionPostcode"]!.GetValue<string>().Should().Be("3087 BM");
        booking["collectionCity"]!.GetValue<string>().Should().Be("Rotterdam");
        booking["collectionCountry"]!.GetValue<string>().Should().Be("NLD");
        booking["deliveryCity"]!.GetValue<string>().Should().Be("Düsseldorf");
        booking["deliveryCountry"]!.GetValue<string>().Should().Be("DEU");
    }

    [Fact]
    public void ToCarrierBooking_SummerTime_GivesLocalTimesWithoutAnOffset()
    {
        var booking = Map(Release());

        booking["collectFrom"]!.GetValue<string>().Should().Be("2030-10-02T07:30:00");
        booking["collectUntil"]!.GetValue<string>().Should().Be("2030-10-02T11:00:00");
        booking["deliverFrom"]!.GetValue<string>().Should().Be("2030-10-03T08:00:00");
        booking["deliverUntil"]!.GetValue<string>().Should().Be("2030-10-03T16:30:00");
    }

    [Fact]
    public void ToCarrierBooking_WinterTime_GivesLocalTimesOneHourAfterUtc()
    {
        var booking = Map(Release("2030-01-15T06:30:00+00:00", "2030-01-15T10:00:00+00:00"));

        booking["collectFrom"]!.GetValue<string>().Should().Be("2030-01-15T07:30:00");
        booking["collectUntil"]!.GetValue<string>().Should().Be("2030-01-15T11:00:00");
    }

    [Fact]
    public void ToCarrierBooking_ARelease_LeavesOutTheShipperAndTheFreightCharge()
    {
        var text = Map(Release()).ToJsonString();

        text.Should().NotContain("SHIPPER-ACME").And.NotContain("ACME-REF-9").And.NotContain("shipperId")
            .And.NotContain("freightCharge").And.NotContain("412").And.NotContain("TO-0000007");
    }

    [Fact]
    public void ToCarrierBooking_ARelease_CarriesTheConsigneeNameAndTheGoodsLines()
    {
        var booking = Map(Release());

        booking["consigneeName"]!.GetValue<string>().Should().Be("Getränke Schmitz GmbH");
        var goods = booking["goods"]!.AsArray();
        goods.Should().HaveCount(2);
        goods[0]!["packageType"]!.GetValue<string>().Should().Be("pallet");
        goods[0]!["quantity"]!.GetValue<int>().Should().Be(4);
        goods[0]!["weightKg"]!.GetValue<decimal>().Should().Be(2400);
        goods[0]!["lengthCm"]!.GetValue<decimal>().Should().Be(120);
        goods[0]!["heightCm"]!.GetValue<decimal>().Should().Be(160);
        goods[1]!.AsObject().ContainsKey("lengthCm").Should().BeFalse();
    }

    [Fact]
    public void ToCarrierBooking_AnUnknownCountry_LeavesTheCountryAndItsTimesOut()
    {
        var booking = Map(Release().Replace("\"country\": \"NL\"", "\"country\": \"ZZ\""));

        booking.ContainsKey("collectionCountry").Should().BeFalse();
        booking.ContainsKey("collectFrom").Should().BeFalse();
        booking["collectionCity"]!.GetValue<string>().Should().Be("Rotterdam");
    }

    [Fact]
    public void ToCarrierBooking_AReleaseWithoutGoodsOrWindows_LeavesThemOut()
    {
        var booking = Map("""
            { "carrierBookingNumber": "CB-0000002", "shipmentNumber": "SH-0000043", "transportOrder": {} }
            """);

        booking.Select(property => property.Key).Should().BeEquivalentTo("carrierBookingNumber", "shipmentNumber");
    }

    [Theory]
    [InlineData("""{ "carrierBookingNumber": null, "shipmentNumber": "SH-0000044" }""")]
    [InlineData("""{ "shipmentNumber": "SH-0000044" }""")]
    [InlineData("""["not a release"]""")]
    [InlineData("not json")]
    public void ToCarrierBooking_NotACarrierRelease_GivesNothing(string message) =>
        CarrierBookingMap.ToCarrierBooking(message).Should().BeNull();

    [Fact]
    public void Alpha3Codes_EveryCountryTheIntakeMapKnows_HasOne()
    {
        var withoutCode = Countries.All.Select(country => country.Code).Where(code => Alpha3Codes.Of(code) is null);

        withoutCode.Should().BeEmpty();
        Alpha3Codes.Alpha2.Should().BeEquivalentTo(Countries.All.Select(country => country.Code));
    }

    [Fact]
    public void Alpha3Codes_EachCode_IsThreeCapitals() =>
        Alpha3Codes.Alpha2.Select(code => Alpha3Codes.Of(code)).Should().OnlyContain(code => code!.Length == 3 && code.All(char.IsAsciiLetterUpper));

    [Fact]
    public void ToCarrierBooking_ARateCardCost_CarriesTheTotalAndItsChargeLinesAsNoSpotPrice()
    {
        var cost = Map(Release())["carrierCost"]!.AsObject();

        cost["currency"]!.GetValue<string>().Should().Be("EUR");
        cost["total"]!.GetValue<decimal>().Should().Be(310.00m);
        cost["isSpot"]!.GetValue<bool>().Should().BeFalse();
        cost["chargeLines"]!.AsArray().Select(line => (line!["type"]!.GetValue<string>(), line["amount"]!.GetValue<decimal>()))
            .Should().Equal(("Freight", 290.00m), ("MinimumChargeAdjustment", 20.00m));
    }

    [Fact]
    public void ToCarrierBooking_ASpotPrice_IsFlaggedAsSpotWithoutTheRemarkOrTheRateCard()
    {
        var cost = Map(Release(carrierCost: SpotCost))["carrierCost"]!.AsObject();

        cost["isSpot"]!.GetValue<bool>().Should().BeTrue();
        cost["total"]!.GetValue<decimal>().Should().Be(480.00m);
        cost.ToJsonString().Should().NotContain("remark").And.NotContain("rateCard").And.NotContain("calculatedAt");
    }

    [Fact]
    public void ToCarrierBooking_AReleaseWithAFreightCharge_NeverCarriesItToTheCarrier()
    {
        var booking = Map(Release());

        booking.ToJsonString().Should().NotContain("freightCharge").And.NotContain("412.5");
    }
}
