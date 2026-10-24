using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.Faults;

namespace Norvia.Tms.Api.IntegrationTests.Faults;

/// <summary>
/// The TMS Shipper tracking API's fault (P8): while it is on the API answers 503 at once, which API Management passes to the Shipper, and nothing is queued. The Carrier-tracking and Driver
/// reports paths, and the TMS's other APIs, are not affected; the hop it caused carries the fault's name.
/// </summary>
public sealed class ShipperTrackingFaultTests(FaultsFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhileTheFaultIsOn_TheShipperTrackingApiAnswers503AtOnce_ForEveryQuery()
    {
        var order = await fixture.SubmitAcceptedForAsync(ShipperTrackingCalls.Shipper, Ct);
        await using var fault = await FaultsFixture.SwitchedOnAsync(fixture.TmsPresenter, TmsFaultNames.ShipperTrackingApiFailing, TimeSpan.FromMinutes(2), Ct);

        using var listed = await fixture.ListAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, Ct);
        using var got = await fixture.GetAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, order, Ct);
        using var found = await fixture.FindAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, "anything", Ct);

        foreach (var response in new[] { listed, got, found })
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        }
    }

    [Fact]
    public async Task WhileTheFaultIsOn_TheHopItCausedIsFailed_WithTheFaultsName_OnTheCallsTrail()
    {
        var correlationId = TmsCalls.NewCorrelationId();
        await using var fault = await FaultsFixture.SwitchedOnAsync(fixture.TmsPresenter, TmsFaultNames.ShipperTrackingApiFailing, TimeSpan.FromMinutes(2), Ct);

        using var refused = await fixture.ListAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, Ct, correlationId);

        refused.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var hop = await fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["fault"] is not null, Ct);
        hop["fault"].Should().Be(TmsFaultNames.ShipperTrackingApiFailing);
        hop["outcome"].Should().Be("Failed");
        hop["failureCode"].Should().Be("ShipperTrackingApiUnavailable");
    }

    [Fact]
    public async Task WhileTheFaultIsOn_TheOtherApisOfTheTmsKeepAnswering()
    {
        await using var fault = await FaultsFixture.SwitchedOnAsync(fixture.TmsPresenter, TmsFaultNames.ShipperTrackingApiFailing, TimeSpan.FromMinutes(2), Ct);

        using var tracking = await fixture.RecordAsync(TrackingEventCalls.Collected("unknown-booking", DateTimeOffset.UtcNow.AddMinutes(-5)), Ct);
        using var planner = await fixture.TmsUi.GetAsync("/v1/shipments/to-release", Ct);
        using var health = await fixture.CreateTmsClient(token: null).GetAsync("/health", Ct);

        tracking.StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable, "the Tracking event API is another gate");
        planner.StatusCode.Should().Be(HttpStatusCode.OK);
        health.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AfterTheFaultIsSwitchedOff_TheShipperGetsTheirTransportOrder()
    {
        var order = await fixture.SubmitAcceptedForAsync(ShipperTrackingCalls.Shipper, Ct);
        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, TmsFaultNames.ShipperTrackingApiFailing, on: true, TimeSpan.FromMinutes(2), Ct);
        using var refused = await fixture.GetAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, order, Ct);
        refused.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, TmsFaultNames.ShipperTrackingApiFailing, on: false, TimeSpan.Zero, Ct);
        using var answered = await fixture.GetAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, order, Ct);

        answered.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await answered.Content.ReadAsStringAsync(Ct))!["transportOrderNumber"]!.GetValue<string>().Should().Be(order);
    }
}
