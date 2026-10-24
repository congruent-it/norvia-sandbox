using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Norvia.DriverApp.Api.IntegrationTests.Faults;

/// <summary>
/// The fault contract (ServiceDefaults) as the Driver app's API serves it under the Aspire test host: a fault switch is open to the Demo.Fault role only,
/// a fault switched on expires by itself, a restart switches every fault off, and the fault shows on the hop it caused. Each test switches its own fault on and off.
/// </summary>
public sealed class FaultSwitchTests(FaultDriverAppFixture driverApp) : IClassFixture<FaultDriverAppFixture>
{
    private const string Fault = "driver-app-unavailable";
    private const string FaultPath = "/v1/faults/" + Fault;
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListingTheFaults_WithoutAToken_IsRefusedWith401()
    {
        using var response = await driverApp.Anonymous.GetAsync("/v1/faults", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SwitchingAFaultOn_WithoutAToken_IsRefusedWith401_AndTheFaultStaysOff()
    {
        using var response = await driverApp.Anonymous.PutAsJsonAsync(FaultPath, new { on = true, durationSeconds = 60 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StatusAsync()).GetProperty("on").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(DriverAppFixture.ReportOutcomeRole)]
    [InlineData(DriverAppFixture.ResetRole)]
    [InlineData("DRV-0001")]
    public async Task SwitchingAFaultOn_WithoutTheFaultRole_IsRefusedWith403_AndTheFaultStaysOff(string role)
    {
        using var caller = driverApp.DriverReportsClient(driverApp.IssueToken([role]));

        using var response = await caller.PutAsJsonAsync(FaultPath, new { on = true, durationSeconds = 60 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StatusAsync()).GetProperty("on").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ListingTheFaults_WithoutTheFaultRole_IsRefusedWith403()
    {
        using var caller = driverApp.DriverReportsClient(driverApp.IssueToken([DriverAppFixture.ReportOutcomeRole]));

        using var response = await caller.GetAsync("/v1/faults", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheTripEndpointsKey_DoesNotOpenAFaultSwitch()
    {
        using var response = await driverApp.DriverApp.PutAsJsonAsync(FaultPath, new { on = true, durationSeconds = 60 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StatusAsync()).GetProperty("on").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AFaultSwitchedOn_ExpiresByItself_AndTheDriverAppAcceptsTripsAgain()
    {
        const string trip = "TR-6000001";
        const string driver = "DRV-6000001";
        await SwitchAsync(on: true, durationSeconds: 3);
        using var refused = await PostTripAsync(trip, driver);
        refused.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await WaitUntilOffAsync();
        using var accepted = await PostTripAsync(trip, driver);

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EveryFault_IsOffByDefault_AfterARestart()
    {
        await SwitchAsync(on: true, durationSeconds: 120);
        (await StatusAsync()).GetProperty("on").GetBoolean().Should().BeTrue();

        await driverApp.RestartDriverAppApiAsync(Ct);

        var faults = (await driverApp.Presenter.GetFromJsonAsync<List<JsonElement>>("/v1/faults", Ct))!;
        faults.Should().NotBeEmpty();
        faults.Should().OnlyContain(fault => !fault.GetProperty("on").GetBoolean());
        using var accepted = await PostTripAsync("TR-6000002", "DRV-6000002");
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AFaultThatRefusesATrip_ShowsAsTheFaultOnTheHopItCaused()
    {
        const string trip = "TR-6000003";
        await SwitchAsync(on: true, durationSeconds: 60);
        try
        {
            using var refused = await PostTripAsync(trip, "DRV-6000003");
            refused.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            await SwitchAsync(on: false);
        }

        var hop = await driverApp.TrackedRecordAsync(trip, "fault", Ct);

        hop.Should().NotBeNull("the refused Trip version is traced with the fault that caused it");
        hop!["fault"].Should().Be(Fault);
        hop["outcome"].Should().Be("Failed");
        hop["flow"].Should().Be("trip-dispatch");
    }

    private async Task<JsonElement> StatusAsync() =>
        (await driverApp.Presenter.GetFromJsonAsync<List<JsonElement>>("/v1/faults", Ct))!.Single(fault => fault.GetProperty("name").GetString() == Fault);

    private async Task SwitchAsync(bool on, int durationSeconds = 60)
    {
        using var response = await driverApp.Presenter.PutAsJsonAsync(FaultPath, new { on, durationSeconds }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task WaitUntilOffAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while ((await StatusAsync()).GetProperty("on").GetBoolean())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }
    }

    private async Task<HttpResponseMessage> PostTripAsync(string trip, string driver) =>
        await driverApp.DriverApp.PostAsync("/v1/trips", new StringContent(await DriverAppFixture.SampleAsync("two-shipments", trip, version: 1, driverId: driver), Encoding.UTF8, "application/json"), Ct);
}
