using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// "Fix the Rate Card, then re-rate" (P2): orders to Düsseldorf fail with NoMatchingLane, the test seed's Rate Card is
/// given Lanes into Düsseldorf and the TMS restarted, and a re-rate then rates them. Rate Cards aren't reloaded while the
/// TMS runs, which is why it restarts.
/// </summary>
[Trait("AppHost", "Own3")]
public sealed class TransportOrderReRateAfterCorrectionTests(CorrectableSeedTmsFixture tms) : IClassFixture<CorrectableSeedTmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReRate_AfterTheRateCardIsCorrected_IsRatedFromTheVersionValidOnTheCollectionDate_AndPublishesOneTransportOrderRated()
    {
        // Collected under version 1 (to 2039) and version 2 (2040 to 2049). Today falls under version 1, so the order
        // collected in 2040 is priced by version 2 only if the re-rate goes by the collection date.
        var (underVersion1, intake1) = await SubmitFailingToDusseldorfAsync(new DateTimeOffset(2030, 6, 3, 8, 0, 0, TimeSpan.Zero));
        var (underVersion2, intake2) = await SubmitFailingToDusseldorfAsync(new DateTimeOffset(2040, 3, 1, 8, 0, 0, TimeSpan.Zero));

        await tms.CorrectSeedAsync(AddLanesToDusseldorf, _cancellationToken);

        var key1 = Guid.NewGuid().ToString();
        foreach (var (number, key) in new[] { (underVersion1, key1), (underVersion2, Guid.NewGuid().ToString()) })
        {
            using var response = await tms.ReRateAsync(number, _cancellationToken, key);
            var result = await response.Content.ReadAsStringAsync(_cancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK, result);
            JsonNode.Parse(result)!["ratingResult"]!.GetValue<string>().Should().Be("Rated");
        }

        var order1 = await tms.GetTransportOrderAsync(underVersion1, _cancellationToken);
        order1["ratingResult"]!.GetValue<string>().Should().Be("Rated");
        order1["ratingFailure"].Should().BeNull();
        order1["freightCharge"]!["rateCardVersion"]!["version"]!.GetValue<int>().Should().Be(1);
        order1["freightCharge"]!["total"]!.GetValue<decimal>().Should().Be(182.10m); // 910.5 kg ÷ 100 × 20.00

        var order2 = await tms.GetTransportOrderAsync(underVersion2, _cancellationToken);
        order2["freightCharge"]!["rateCardVersion"]!["version"]!.GetValue<int>().Should().Be(2);
        order2["freightCharge"]!["total"]!.GetValue<decimal>().Should().Be(227.63m); // 910.5 kg ÷ 100 × 25.00 = 227.625

        // A retry of the re-rate that rated it answers Rated again, where a new re-rate of a Rated order is refused.
        using (var retry = await tms.ReRateAsync(underVersion1, _cancellationToken, key1))
        {
            retry.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonNode.Parse(await retry.Content.ReadAsStringAsync(_cancellationToken))!["repeat"]!.GetValue<bool>().Should().BeTrue();
        }

        var rated = (await tms.PublishedAsync(intake1, "TransportOrderRated", _cancellationToken)).Should().ContainSingle().Subject;
        rated.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["correlationId"] = intake1,
            ["transportOrderNumber"] = underVersion1,
            ["flow"] = "transport-order-rating",
            ["partyId"] = "SHP-0001",
        });
        var body = JsonNode.Parse(rated.Body.ToString())!;
        CanonicalModelSchemas.ValidateTransportOrderRated(body).IsValid.Should().BeTrue();
        JsonNode.DeepEquals(body["freightCharge"], order1["freightCharge"]).Should().BeTrue();
        (await tms.PublishedAsync(intake2, "TransportOrderRated", _cancellationToken)).Should().ContainSingle();
        (await tms.ListRatingExceptionsAsync(_cancellationToken)).Should().NotContain(e =>
            e!["transportOrderNumber"]!.GetValue<string>() == underVersion1 || e["transportOrderNumber"]!.GetValue<string>() == underVersion2);
    }

    private async Task<(string Number, string Intake)> SubmitFailingToDusseldorfAsync(DateTimeOffset collectedFrom)
    {
        var intake = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(
            Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf().CollectedFrom(collectedFrom), intake, _cancellationToken);
        var failed = await tms.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);
        failed["ratingFailure"]!["code"]!.GetValue<string>().Should().Be("NoMatchingLane");
        await tms.PublishedAsync(intake, "TransportOrderRatingFailed", _cancellationToken);
        return (number, intake);
    }

    private static void AddLanesToDusseldorf(JsonObject seed)
    {
        var versions = seed["rateCards"]!.AsArray().Single(card => card!["id"]!.GetValue<string>() == "RC-TEST-0001")!["versions"]!.AsArray();
        foreach (var version in versions)
        {
            var zones = version!["zones"]!.AsArray();
            if (!zones.Any(zone => zone!["name"]!.GetValue<string>() == "Düsseldorf"))
            {
                zones.Add(new JsonObject { ["name"] = "Düsseldorf", ["country"] = "DE", ["postcodePrefixes"] = new JsonArray("40") });
            }

            var pricePer100Kg = version["version"]!.GetValue<int>() == 1 ? 20.00m : 25.00m;
            version["lanes"]!.AsArray().Add(new JsonObject
            {
                ["origin"] = "Rotterdam",
                ["destination"] = "Düsseldorf",
                ["minimumCharge"] = 60.00m,
                ["weightBreaks"] = new JsonArray(new JsonObject { ["upToKg"] = 24000, ["pricePer100Kg"] = pricePer100Kg }),
            });
        }
    }
}
