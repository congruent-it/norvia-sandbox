using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>A Rate Card edited in the ERP and replaced in the TMS changes only what is rated after it arrives.</summary>
public sealed class RatingTests(MasterDataSyncFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOrderRatedBeforeAReplacement_KeepsItsFreightCharge_AndOneRatedAfterUsesTheNewVersion()
    {
        // A Shipper with a card of its own: every shipment of 100 kg is priced at the lane's minimum charge, 65 then 130.
        var shipper = Kinds.Shipper.NewId();
        await fixture.SaveAsync(Kinds.Shipper, shipper, await Kinds.Shipper.NewBody(fixture, shipper, Ct), ct: Ct);
        var card = Kinds.RateCard.NewId();
        var cardBody = new JsonObject { ["shipperId"] = shipper, ["versions"] = new JsonArray(Cards.Version(1, "2026-01-01", null, 65m)) };
        (await fixture.SaveAsync(Kinds.RateCard, card, cardBody, ct: Ct)).Status.Should().Be(HttpStatusCode.Created);
        await fixture.ReplicaAsync(Kinds.Shipper, shipper, 1, Ct);
        await fixture.ReplicaAsync(Kinds.RateCard, card, 1, Ct);
        var before = await SubmitAndRateAsync(shipper);

        cardBody["versions"] = new JsonArray(Cards.Version(1, "2026-01-01", null, 130m));
        (await fixture.SaveAsync(Kinds.RateCard, card, cardBody, ct: Ct)).Status.Should().Be(HttpStatusCode.OK);
        await fixture.ReplicaAsync(Kinds.RateCard, card, 2, Ct);
        var after = await SubmitAndRateAsync(shipper);

        var stillBefore = await fixture.GetTransportOrderAsync(before.Number, Ct);
        JsonNode.DeepEquals(stillBefore["freightCharge"], before.Charge).Should().BeTrue("the order rated before the replacement keeps its Freight Charge");
        JsonNode.DeepEquals(after.Charge, before.Charge).Should().BeFalse("the order rated after the replacement uses the new version");
        after.Charge.ToJsonString().Should().Contain("130").And.NotContain("65");
        before.Charge.ToJsonString().Should().Contain("65");
    }

    private async Task<(string Number, JsonNode Charge)> SubmitAndRateAsync(string shipperId)
    {
        var booking = Bookings.Valid(TmsCalls.NewReference()).Weighing(100m);
        booking["shipperId"] = shipperId;
        var number = await fixture.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), Ct);
        await fixture.WaitForRatingToSettleAsync(number, Ct);
        var order = await fixture.GetTransportOrderAsync(number, Ct);
        order["freightCharge"].Should().NotBeNull("the order is rated: " + order.ToJsonString());
        return (number, order["freightCharge"]!.DeepClone());
    }
}
