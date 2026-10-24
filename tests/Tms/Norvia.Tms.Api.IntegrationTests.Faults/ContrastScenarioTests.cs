using System.Net;
using Norvia.Tms.Api.Faults;

namespace Norvia.Tms.Api.IntegrationTests.Faults;

/// <summary>
/// The runtime part of the contrast (docs/operations/as-found.md): the TMS fails for a few seconds, both the Shipper's read and the Carrier's event hit it, and the failure is loud and nothing
/// is lost. The Shipper is told at once and nothing is queued, the Carrier's event waits on the queue and is recorded when the TMS is back, and the dead-letter queue holds nothing of it.
/// The test books a Shipment of its own.
/// </summary>
public sealed class ContrastScenarioTests(FaultsFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Journeys Journeys => new(fixture);

    [Fact]
    public async Task WhenTheTmsFailsForAFewSeconds_TheShipperIsToldAtOnce_AndTheCarriersEventIsRecordedAfterwards_WithNothingDeadLettered()
    {
        var (shipment, _, reference) = await fixture.BookedShipmentWithNumberAsync(Ct);
        var eventTrail = TmsCalls.NewCorrelationId();
        var shipperTrail = TmsCalls.NewCorrelationId();
        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, TmsFaultNames.ShipperTrackingApiFailing, on: true, TimeSpan.FromSeconds(5), Ct);
        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, TmsFaultNames.TrackingEventApiFailing, on: true, TimeSpan.FromSeconds(5), Ct);

        using var shipperRead = await fixture.ListAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, Ct, shipperTrail);
        await Journeys.SendCollectedAsync(reference, eventTrail, Ct);
        var refusal = await fixture.TmsLogAsync(log => log["correlationId"] == eventTrail && log["fault"] == TmsFaultNames.TrackingEventApiFailing, Ct);

        shipperRead.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "the Shipper gets the error and not a stale answer");
        (await fixture.TmsLogAsync(log => log["correlationId"] == shipperTrail && log["fault"] is not null, Ct))["outcome"].Should().Be("Failed");
        refusal["outcome"].Should().Be("Failed", "the failure is loud on the console's trail");
        (await Journeys.ShipmentStatusAsync(shipment, Ct)).Should().Be("Booked", "the TMS recorded nothing while it was failing");

        await FaultsFixture.WaitUntilOffAsync(fixture.TmsPresenter, TmsFaultNames.TrackingEventApiFailing, Ct);
        await FaultsFixture.WaitUntilOffAsync(fixture.TmsPresenter, TmsFaultNames.ShipperTrackingApiFailing, Ct);
        await Eventually.UntilAsync(async () => await Journeys.ShipmentStatusAsync(shipment, Ct) == "Collected", Ct);
        await TmsFixture.SettleOutboxAsync(Ct);

        (await fixture.GetTrackingTimelineAsync(shipment, Ct)).Should().ContainSingle(trackingEvent => trackingEvent!["type"]!.GetValue<string>() == "Collected");
        (await fixture.DeadLetteredAsync(message => message.CorrelationId == eventTrail, TimeSpan.FromSeconds(3), Ct)).Should().BeNull("nothing is lost, and nothing is dead-lettered");
        using var shipperAgain = await fixture.ListAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, Ct);
        shipperAgain.StatusCode.Should().Be(HttpStatusCode.OK, "the Shipper reads again once the TMS is back");
    }
}
