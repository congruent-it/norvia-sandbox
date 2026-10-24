using System.Net;
using Norvia.Tms.Api.Faults;

namespace Norvia.Tms.Api.IntegrationTests.Faults;

/// <summary>
/// The TMS Tracking event API's fault (P5): while it is on the API answers 503, and a Carrier's message waits on the queue: the Carrier-tracking worker abandons it, the queue delivers it again, and
/// once the fault is off the TMS records it, so nothing is lost. Each test books a Shipment of its own.
/// </summary>
public sealed class TrackingEventApiFaultTests(FaultsFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Journeys Journeys => new(fixture);

    [Fact]
    public async Task WhileTheFaultIsOn_TheTrackingEventApiAnswers503_AndTheTmsHopIsFailedWithTheFault()
    {
        var correlationId = TmsCalls.NewCorrelationId();
        await using var fault = await FaultsFixture.SwitchedOnAsync(fixture.TmsPresenter, TmsFaultNames.TrackingEventApiFailing, TimeSpan.FromMinutes(2), Ct);

        using var refused = await fixture.RecordAsync(TrackingEventCalls.Collected("any-reference", DateTimeOffset.UtcNow.AddMinutes(-5)), Ct, correlationId);

        refused.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var hop = await fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["fault"] is not null, Ct);
        hop["fault"].Should().Be(TmsFaultNames.TrackingEventApiFailing);
        hop["outcome"].Should().Be("Failed");
        hop["failureCode"].Should().Be("TrackingEventApiUnavailable");
    }

    [Fact]
    public async Task AMessageOnTheQueue_WaitsWhileTheFaultIsOn_AndIsRecordedAfterwards_WithNothingLost()
    {
        var (shipment, _, reference) = await fixture.BookedShipmentWithNumberAsync(Ct);
        var correlationId = TmsCalls.NewCorrelationId();
        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, TmsFaultNames.TrackingEventApiFailing, on: true, TimeSpan.FromMinutes(2), Ct);

        await Journeys.SendCollectedAsync(reference, correlationId, Ct);
        var refusal = await fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["fault"] == TmsFaultNames.TrackingEventApiFailing, Ct);

        refusal["outcome"].Should().Be("Failed");
        (await Journeys.ShipmentStatusAsync(shipment, Ct)).Should().Be("Booked", "the TMS recorded nothing while the fault was on");

        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, TmsFaultNames.TrackingEventApiFailing, on: false, TimeSpan.Zero, Ct);
        await Eventually.UntilAsync(async () => await Journeys.ShipmentStatusAsync(shipment, Ct) == "Collected", Ct);

        (await fixture.GetTrackingTimelineAsync(shipment, Ct)).Should().ContainSingle(trackingEvent => trackingEvent!["type"]!.GetValue<string>() == "Collected");
        (await fixture.DeadLetteredAsync(message => message.CorrelationId == correlationId, TimeSpan.FromSeconds(3), Ct)).Should().BeNull("a message that waited is not a dead letter");
    }

    [Fact]
    public async Task AMessageOnTheQueue_IsRecordedOnce_AfterTheFaultExpiresByItself()
    {
        var (shipment, _, reference) = await fixture.BookedShipmentWithNumberAsync(Ct);
        var correlationId = TmsCalls.NewCorrelationId();
        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, TmsFaultNames.TrackingEventApiFailing, on: true, TimeSpan.FromSeconds(5), Ct);

        await Journeys.SendCollectedAsync(reference, correlationId, Ct);
        await fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["fault"] == TmsFaultNames.TrackingEventApiFailing, Ct);
        await FaultsFixture.WaitUntilOffAsync(fixture.TmsPresenter, TmsFaultNames.TrackingEventApiFailing, Ct);

        await Eventually.UntilAsync(async () => await Journeys.ShipmentStatusAsync(shipment, Ct) == "Collected", Ct);
        await TmsFixture.SettleOutboxAsync(Ct);
        (await fixture.GetTrackingTimelineAsync(shipment, Ct)).Should().ContainSingle(trackingEvent => trackingEvent!["type"]!.GetValue<string>() == "Collected");
    }
}
