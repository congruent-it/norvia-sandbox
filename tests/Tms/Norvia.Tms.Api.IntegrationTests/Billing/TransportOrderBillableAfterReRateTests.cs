using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Billing;

/// <summary>
/// A Transport Order whose Rating failed is delivered and has its Proof of delivery, so the Rating is the one condition
/// left (P9). The Rate Card is corrected, and the re-rate (P2) then makes the order Billable. Rate Cards aren't reloaded
/// while the TMS runs, which is why it restarts.
/// </summary>
[Trait("AppHost", "Own2")]
public sealed class TransportOrderBillableAfterReRateTests(CorrectableSeedTmsFixture tms) : IClassFixture<CorrectableSeedTmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReRate_AfterRatingFailedOfADeliveredOrderWithItsProofOfDelivery_MakesItBillable_AndPublishesOneTransportOrderBillable()
    {
        var intake = TmsCalls.NewCorrelationId();
        var order = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), intake, _cancellationToken);
        (await tms.WaitForRatingResultAsync(order, "RatingFailed", _cancellationToken))["ratingFailure"]!["code"]!.GetValue<string>().Should().Be("NoMatchingLane");
        var booked = await tms.BookedAsync(order, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        var proofCorrelationId = TmsCalls.NewCorrelationId();
        await tms.ProofRecordedAsync(booked, proofCorrelationId, _cancellationToken);
        await tms.PublishedAsync(proofCorrelationId, "ProofOfDeliveryReceived", _cancellationToken);
        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("NotYetBillable", "Rating failed, so the order isn't Rated");
        tms.BillablePublished(order).Should().BeEmpty();

        await tms.CorrectSeedAsync(AddLanesToDusseldorf, _cancellationToken);
        using var response = await tms.ReRateAsync(order, _cancellationToken);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var message = (await tms.PublishedAsync(intake, "TransportOrderBillable", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        CanonicalModelSchemas.ValidateTransportOrderBillable(body).IsValid.Should().BeTrue();
        body["transportOrderNumber"]!.GetValue<string>().Should().Be(order);
        body["correlationId"]!.GetValue<string>().Should().Be(intake, "a re-rate is on its Transport Order's intake trail");
        body["intakeCorrelationId"]!.GetValue<string>().Should().Be(intake);
        var stored = await tms.GetTransportOrderAsync(order, _cancellationToken);
        JsonNode.DeepEquals(body["freightCharge"], stored["freightCharge"]).Should().BeTrue();
        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("Billable");
        tms.BillablePublished(order).Should().ContainSingle();
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

            version["lanes"]!.AsArray().Add(new JsonObject
            {
                ["origin"] = "Rotterdam",
                ["destination"] = "Düsseldorf",
                ["minimumCharge"] = 60.00m,
                ["weightBreaks"] = new JsonArray(new JsonObject { ["upToKg"] = 24000, ["pricePer100Kg"] = 20.00m }),
            });
        }
    }
}
