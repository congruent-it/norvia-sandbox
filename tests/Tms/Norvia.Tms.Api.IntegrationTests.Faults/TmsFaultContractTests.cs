using System.Net;
using System.Net.Http.Json;
using Norvia.AppHost;
using Norvia.Tms.Api.Faults;

namespace Norvia.Tms.Api.IntegrationTests.Faults;

/// <summary>
/// The fault contract (ServiceDefaults) as the TMS API serves it under the Aspire test host: a fault switch is open to the Demo.Fault role only, every fault is off by default and after
/// a restart, and a fault switched on expires by itself. Each test switches its own fault on and off.
/// </summary>
public sealed class TmsFaultContractTests(FaultsFixture fixture)
{
    private static readonly string[] Faults = [TmsFaultNames.Unavailable, TmsFaultNames.TrackingEventApiFailing, TmsFaultNames.ShipperTrackingApiFailing];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryFault_IsListedAndOffByDefault()
    {
        var faults = (await fixture.TmsPresenter.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/v1/faults", Ct))!;

        faults.Select(fault => fault.GetProperty("name").GetString()).Should().BeEquivalentTo(Faults);
        faults.Should().OnlyContain(fault => !fault.GetProperty("on").GetBoolean());
        faults.Should().OnlyContain(fault => fault.GetProperty("owner").GetString() == "tms");
    }

    [Fact]
    public async Task ListingTheFaults_WithoutAToken_IsRefusedWith401()
    {
        using var anonymous = fixture.CreateTmsClient(token: null);

        using var response = await anonymous.GetAsync("/v1/faults", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(TmsFixture.IntakeRole)]
    [InlineData(TmsFixture.ManageRole)]
    [InlineData(TmsFixture.ShipperTrackingReadRole)]
    [InlineData(TmsFixture.TrackingRecordRole)]
    public async Task SwitchingAFaultOn_WithoutTheFaultRole_IsRefusedWith403_AndNoGateOpens(string role)
    {
        using var caller = fixture.CreateTmsClient(fixture.IssueToken([role], clientId: LocalTokens.PartnerPortalClientId));

        foreach (var fault in Faults)
        {
            using var switched = await caller.PutAsJsonAsync($"/v1/faults/{fault}", new { on = true, durationSeconds = 60 }, Ct);
            switched.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await FaultsFixture.StatusAsync(fixture.TmsPresenter, fault, Ct)).GetProperty("on").GetBoolean().Should().BeFalse();
        }

        using var listed = await caller.GetAsync("/v1/faults", Ct);
        listed.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var calls = await fixture.ListAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, Ct);
        calls.StatusCode.Should().Be(HttpStatusCode.OK, "nothing the refused caller did made the TMS fail");
    }

    [Fact]
    public async Task SwitchingAFaultOn_WithATokenForAnotherAudience_IsRefusedWith401()
    {
        using var caller = fixture.CreateTmsClient(fixture.IssueToken([FaultsFixture.FaultRole], audience: LocalTokens.DriverAppAudience, clientId: LocalTokens.PartnerPortalClientId));

        using var switched = await caller.PutAsJsonAsync($"/v1/faults/{TmsFaultNames.Unavailable}", new { on = true, durationSeconds = 60 }, Ct);

        switched.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await FaultsFixture.StatusAsync(fixture.TmsPresenter, TmsFaultNames.Unavailable, Ct)).GetProperty("on").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AFaultSwitchedOn_ExpiresByItself_AndTheTmsAnswersAgain()
    {
        const string fault = TmsFaultNames.ShipperTrackingApiFailing;
        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, fault, on: true, TimeSpan.FromSeconds(3), Ct);
        using var refused = await fixture.ListAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, Ct);
        refused.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await FaultsFixture.WaitUntilOffAsync(fixture.TmsPresenter, fault, Ct);
        using var answered = await fixture.ListAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, Ct);

        answered.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EveryFault_IsOffByDefault_AfterARestart()
    {
        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, TmsFaultNames.TrackingEventApiFailing, on: true, TimeSpan.FromMinutes(2), Ct);
        await FaultsFixture.SwitchAsync(fixture.TmsPresenter, TmsFaultNames.ShipperTrackingApiFailing, on: true, TimeSpan.FromMinutes(2), Ct);

        await fixture.RestartAsync(Ct);

        var faults = (await fixture.TmsPresenter.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/v1/faults", Ct))!;
        faults.Should().HaveCount(Faults.Length).And.OnlyContain(fault => !fault.GetProperty("on").GetBoolean());
        using var answered = await fixture.ListAsync(fixture.ShipperTracking, ShipperTrackingCalls.Shipper, Ct);
        answered.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
