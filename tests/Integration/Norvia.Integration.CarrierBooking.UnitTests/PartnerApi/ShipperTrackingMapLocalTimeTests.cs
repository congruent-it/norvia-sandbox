using System.Globalization;
using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.PartnerApi;

/// <summary>
/// The Liquid maps have no time zone support, so they carry the clock rules of Europe themselves (ADR 0035). These tests
/// check them against the time zone rules of the machine, in every country the map knows, around the days the clocks
/// change and the ends of the months and years.
/// </summary>
public sealed class ShipperTrackingMapLocalTimeTests
{
    private const string LocalFormat = "dd-MM-yyyy HH:mm";

    /// <summary>The countries of Europe the maps know, with the time zone of their mainland.</summary>
    private static readonly (string Country, string TimeZone)[] Countries =
    [
        ("AL", "Europe/Tirane"), ("AD", "Europe/Andorra"), ("AT", "Europe/Vienna"), ("BY", "Europe/Minsk"), ("BE", "Europe/Brussels"),
        ("BA", "Europe/Sarajevo"), ("BG", "Europe/Sofia"), ("HR", "Europe/Zagreb"), ("CY", "Asia/Nicosia"), ("CZ", "Europe/Prague"),
        ("DK", "Europe/Copenhagen"), ("EE", "Europe/Tallinn"), ("FI", "Europe/Helsinki"), ("FR", "Europe/Paris"), ("DE", "Europe/Berlin"),
        ("GR", "Europe/Athens"), ("HU", "Europe/Budapest"), ("IS", "Atlantic/Reykjavik"), ("IE", "Europe/Dublin"), ("IT", "Europe/Rome"),
        ("LV", "Europe/Riga"), ("LI", "Europe/Vaduz"), ("LT", "Europe/Vilnius"), ("LU", "Europe/Luxembourg"), ("MT", "Europe/Malta"),
        ("MD", "Europe/Chisinau"), ("MC", "Europe/Monaco"), ("ME", "Europe/Podgorica"), ("NL", "Europe/Amsterdam"), ("MK", "Europe/Skopje"),
        ("NO", "Europe/Oslo"), ("PL", "Europe/Warsaw"), ("PT", "Europe/Lisbon"), ("RO", "Europe/Bucharest"), ("SM", "Europe/San_Marino"),
        ("RS", "Europe/Belgrade"), ("SK", "Europe/Bratislava"), ("SI", "Europe/Ljubljana"), ("ES", "Europe/Madrid"), ("SE", "Europe/Stockholm"),
        ("CH", "Europe/Zurich"), ("TR", "Europe/Istanbul"), ("UA", "Europe/Kiev"), ("GB", "Europe/London"),
    ];

    public static TheoryData<string, string> CountryTimeZones => new(Countries.Select(country => (country.Country, country.TimeZone)));

    [Theory]
    [MemberData(nameof(CountryTimeZones))]
    public void List_InstantsAroundClockChangesAndMonthEnds_AreOnTheCountrysClocks(string country, string timeZoneId)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var instants = Instants().ToList();
        var canonical = new JsonArray(instants.Select(instant => (JsonNode)new JsonObject
        {
            ["transportOrderNumber"] = "TO-0000001",
            ["shipperReference"] = null,
            ["collection"] = new JsonObject { ["city"] = null, ["countryCode"] = null },
            ["delivery"] = new JsonObject { ["city"] = null, ["countryCode"] = country },
            ["trackingStatus"] = "InTransit",
            ["eta"] = instant.ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture),
            ["lastUpdate"] = null,
        }).ToArray());

        var items = ShipperTrackingMaps.List(canonical.ToJsonString()).AsArray();

        var actual = items.Select(item => (string?)item!["eta"]).ToList();
        var expected = instants.Select(instant => TimeZoneInfo.ConvertTime(instant, timeZone).ToString(LocalFormat, CultureInfo.InvariantCulture)).ToList();
        actual.Should().Equal(expected);
    }

    [Fact]
    public void List_AnUnknownCountry_IsOnUtc()
    {
        var canonical = Item("ZZ", "2030-07-01T10:30:00+00:00");

        var items = ShipperTrackingMaps.List(canonical).AsArray();

        ((string?)items[0]!["eta"]).Should().Be("01-07-2030 10:30");
    }

    [Fact]
    public void Detail_AnEventWithoutACountry_IsOnTheDeliveryAddressClocks()
    {
        var canonical = JsonNode.Parse(Contracts.Read("PartnerApi/samples/shipper-tracking/in-transit-summer.shipper-transport-order-detail.json"))!.AsObject();
        canonical["progress"]!.AsArray()[0]!["place"] = new JsonObject { ["city"] = null, ["countryCode"] = null };

        var tracking = ShipperTrackingMaps.Detail(canonical.ToJsonString());

        // 07:10 UTC in the Netherlands is 09:10; the delivery address is in Germany, which is on the same clocks in July.
        ((string?)tracking["events"]!.AsArray()[0]!["time"]).Should().Be("01-07-2030 09:10");
    }

    [Fact]
    public void Detail_AnEventWithoutAPlace_LeavesOutItsCityAndCountry()
    {
        var canonical = JsonNode.Parse(Contracts.Read("PartnerApi/samples/shipper-tracking/in-transit-summer.shipper-transport-order-detail.json"))!.AsObject();
        canonical["progress"]!.AsArray()[0]!["place"] = new JsonObject { ["city"] = null, ["countryCode"] = null };

        var tracking = ShipperTrackingMaps.Detail(canonical.ToJsonString());

        tracking["events"]!.AsArray()[0]!.AsObject().Select(property => property.Key).Should().BeEquivalentTo("code", "time");
    }

    [Fact]
    public void Detail_TextWithQuotesBackslashesAndControlCharacters_IsEscapedIntoValidJson()
    {
        const string Text = "a \"quoted\" \\ back\nline\ttab";
        var canonical = JsonNode.Parse(Contracts.Read("PartnerApi/samples/shipper-tracking/delivered-winter.shipper-transport-order-detail.json"))!.AsObject();
        canonical["proofOfDelivery"]!["receivedBy"] = Text;

        var tracking = ShipperTrackingMaps.Detail(canonical.ToJsonString());

        ((string?)tracking["proofOfDelivery"]!["receivedBy"]).Should().Be(Text);
    }

    [Fact]
    public void Detail_EventsInTheirOwnTimeZones_AreNotOnTheDeliveryAddressClocks()
    {
        var canonical = JsonNode.Parse(Contracts.Read("PartnerApi/samples/shipper-tracking/delivered-winter.shipper-transport-order-detail.json"))!.AsObject();

        var tracking = ShipperTrackingMaps.Detail(canonical.ToJsonString());

        // Hamburg is an hour ahead of London in December: the delivery address's clocks must not be used for the event.
        ((string?)tracking["events"]!.AsArray()[0]!["time"]).Should().Be("09-12-2030 09:30");
        ((string?)tracking["events"]!.AsArray()[4]!["time"]).Should().Be("11-12-2030 10:05");
    }

    [Fact]
    public void List_CanonicalFieldsTheFormatHasNoPlaceFor_AreNotPassedOn()
    {
        var canonical = JsonNode.Parse(Item("NL", "2030-07-01T10:30:00+00:00"))!.AsArray();
        canonical[0]!["carrier"] = "Kessler Spedition";
        canonical[0]!["shipmentNumber"] = "SH-0000041";

        var items = ShipperTrackingMaps.List(canonical.ToJsonString());

        Schemas.ShipperTrackingListItem.Validate(items.AsArray()[0]).IsValid.Should().BeTrue();
        items.ToJsonString().Should().NotContain("Kessler").And.NotContain("SH-0000041");
    }

    /// <summary>
    /// Two years, one of them a leap year: the last evening of every month, and the minutes around 00:00 and 01:00 UTC of the last Sundays of
    /// March and October, when the clocks change in the countries that change them.
    /// </summary>
    private static IEnumerable<DateTime> Instants()
    {
        foreach (var year in new[] { 2028, 2030 })
        {
            for (var month = 1; month <= 12; month++)
            {
                var last = new DateTime(year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);
                yield return last.AddHours(23).AddMinutes(30);
            }

            foreach (var month in new[] { 3, 10 })
            {
                var day = new DateTime(year, month, 31, 0, 0, 0, DateTimeKind.Utc);
                while (day.DayOfWeek != DayOfWeek.Sunday)
                {
                    day = day.AddDays(-1);
                }

                foreach (var hour in new[] { 0, 1 })
                {
                    yield return day.AddHours(hour).AddMinutes(-1);
                    yield return day.AddHours(hour);
                    yield return day.AddHours(hour).AddMinutes(1);
                }
            }
        }
    }

    private static string Item(string deliveryCountry, string eta) =>
        $$"""
        [{ "transportOrderNumber": "TO-0000001", "shipperReference": null, "collection": { "city": null, "countryCode": null },
           "delivery": { "city": null, "countryCode": "{{deliveryCountry}}" }, "trackingStatus": "InTransit", "eta": "{{eta}}", "lastUpdate": null }]
        """;

}
