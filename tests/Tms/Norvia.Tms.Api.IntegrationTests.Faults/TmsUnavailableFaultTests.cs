using System.Net;
using Norvia.Tms.Api.Faults;

namespace Norvia.Tms.Api.IntegrationTests.Faults;

/// <summary>
/// The TMS unavailable fault (P12): while it is on, every call to the TMS API answers 503 and does nothing, except the way out of the fault and the health endpoints. The Driver's reports
/// are not lost: the Driver reports worker retries, the Trip's later reports wait behind the one in the session, and once the fault is off they are processed in order.
/// </summary>
public sealed class TmsUnavailableFaultTests(FaultsFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Journeys Journeys => new(fixture);

    [Fact]
    public async Task WhileTheFaultIsOn_EveryCallToTheTmsApiAnswers503_ExceptTheWayOutAndTheHealthEndpoint()
    {
        await using var fault = await FaultsFixture.SwitchedOnAsync(fixture.TmsPresenter, TmsFaultNames.Unavailable, TimeSpan.FromMinutes(2), Ct);

        using var planner = await fixture.TmsUi.GetAsync("/v1/shipments/to-release", Ct);
        using var intake = await fixture.Tms.GetAsync("/v1/transport-orders/TO-0000001", Ct);
        using var shipperTracking = await fixture.ListAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, Ct);
        using var trackingEvents = await fixture.RecordAsync(TrackingEventCalls.Collected("any-reference", DateTimeOffset.UtcNow.AddMinutes(-5)), Ct);
        using var health = await fixture.CreateTmsClient(token: null).GetAsync("/health", Ct);

        foreach (var response in new[] { planner, intake, shipperTracking, trackingEvents })
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }

        health.StatusCode.Should().Be(HttpStatusCode.OK);
        (await FaultsFixture.StatusAsync(fixture.TmsPresenter, TmsFaultNames.Unavailable, Ct)).GetProperty("on").GetBoolean().Should().BeTrue("the way out stays open");
    }

    [Fact]
    public async Task WhileTheFaultIsOn_TheHopItCausedIsFailedWithTheFault_AndTheMoreSpecificFaultNamesItsOwn()
    {
        var correlationId = TmsCalls.NewCorrelationId();
        var specificId = TmsCalls.NewCorrelationId();
        await using var all = await FaultsFixture.SwitchedOnAsync(fixture.TmsPresenter, TmsFaultNames.Unavailable, TimeSpan.FromMinutes(2), Ct);
        await using var specific = await FaultsFixture.SwitchedOnAsync(fixture.TmsPresenter, TmsFaultNames.ShipperTrackingApiFailing, TimeSpan.FromMinutes(2), Ct);

        using var refused = await fixture.RecordAsync(TrackingEventCalls.Collected("any-reference", DateTimeOffset.UtcNow.AddMinutes(-5)), Ct, correlationId);
        using var shipper = await fixture.ListAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, Ct, specificId);

        refused.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        shipper.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var hop = await fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["fault"] is not null, Ct);
        hop["fault"].Should().Be(TmsFaultNames.Unavailable);
        hop["outcome"].Should().Be("Failed");
        hop["failureCode"].Should().Be("TmsUnavailable");
        (await fixture.TmsLogAsync(log => log["correlationId"] == specificId && log["fault"] is not null, Ct))["fault"].Should().Be(TmsFaultNames.ShipperTrackingApiFailing);
    }

    [Fact]
    public async Task TheDriversReports_WaitInTheirSession_WhileTheFaultIsOn_AndAreProcessedInOrderAfterwards_WithNothingLost()
    {
        var trip = await Journeys.DispatchedTripAsync(FaultsFixture.TestDriver, "VEH-0002", Ct);
        await Journeys.StartedAsync(trip, Ct);
        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, TmsFaultNames.Unavailable, on: true, TimeSpan.FromSeconds(8), Ct);

        var collectedTrail = await Journeys.CollectedAsync(trip, Ct);
        var deliveredTrail = await Journeys.DeliveredAsync(trip, photos: 1, Ct);
        var refusal = await fixture.TmsLogAsync(log => log["correlationId"] == collectedTrail && log["fault"] == TmsFaultNames.Unavailable, Ct);

        refusal["outcome"].Should().Be("Failed");
        (await Journeys.ReportAsync(trip, "Collected", Ct))["state"]!.GetValue<string>().Should().Be("Sending", "the report waits while the TMS is down");
        (await Journeys.ReportAsync(trip, "Delivered", Ct))["state"]!.GetValue<string>().Should().Be("Sending");

        await FaultsFixture.WaitUntilOffAsync(fixture.TmsPresenter, TmsFaultNames.Unavailable, Ct);
        await Journeys.ReportStateAsync(trip, "Collected", "Recorded", Ct);
        await Journeys.ReportStateAsync(trip, "Delivered", "Recorded", Ct);
        await Journeys.ReportStateAsync(trip, "ProofOfDelivery", "Recorded", Ct);

        (await Journeys.ShipmentStatusAsync(trip.ShipmentNumber, Ct)).Should().Be("Delivered");
        (await fixture.GetTimelineAsync(trip.ShipmentNumber, Ct))["proofsOfDelivery"]!.AsArray().Should().ContainSingle("the Proof of delivery was kept, with the photo the Driver took");
        var events = (await fixture.GetTrackingTimelineAsync(trip.ShipmentNumber, Ct)).Select(trackingEvent => trackingEvent!["type"]!.GetValue<string>());
        events.Should().Equal("Collected", "Delivered");
        deliveredTrail.Should().NotBe(collectedTrail);
    }
}
