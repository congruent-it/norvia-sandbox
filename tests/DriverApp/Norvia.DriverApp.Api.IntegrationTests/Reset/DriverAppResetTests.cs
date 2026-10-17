using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.DriverApp.Api.IntegrationTests.Reset;

/// <summary>
/// The Simulator's reset at the Driver app API (ADR 0039): with the setting on and the Simulator's role, the Driver app deletes the Trips it was sent and
/// the Drivers' reports. It shares the Driver app with the other tests, so it runs with them in turn.
/// </summary>
[Collection("Driver app")]
public sealed class DriverAppResetTests(DriverAppFixture driverApp)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AResetWithTheResetRole_DeletesTheTripsAndTheDriversReports_AndTheDriverCanBeSentATripAgain()
    {
        const string driverId = "DRV-5000001";
        await driverApp.ReceiveTripAsync("TR-5000001", driverId, Ct);
        using var driver = driverApp.DriverReportsClient(driverApp.IssueToken([driverId]));
        using var started = await driver.PostAsync("/v1/driver/trips/TR-5000001/start", content: null, Ct);
        started.StatusCode.Should().Be(HttpStatusCode.OK);
        await driverApp.FirstPublishedForAsync("TR-5000001", Ct);
        (await driver.GetFromJsonAsync<JsonArray>("/v1/driver/trips", Ct)).Should().ContainSingle();
        using var simulator = driverApp.DriverReportsClient(driverApp.IssueToken([DriverAppFixture.ResetRole]));

        var deleted = await simulator.ResetAsync(Ct);

        deleted["Trips"].Should().BePositive();
        deleted["Driver reports"].Should().BePositive();
        deleted["Messages"].Should().BePositive();
        (await driver.GetFromJsonAsync<JsonArray>("/v1/driver/trips", Ct)).Should().BeEmpty();
        await driverApp.ReceiveTripAsync("TR-5000002", driverId, Ct);
        (await driver.GetFromJsonAsync<JsonArray>("/v1/driver/trips", Ct))!.Select(trip => trip!["tripNumber"]!.GetValue<string>()).Should().Equal("TR-5000002");
    }

    [Fact]
    public async Task AResetOnAnEmptyDriverApp_Succeeds_WithNothingDeleted()
    {
        using var simulator = driverApp.DriverReportsClient(driverApp.IssueToken([DriverAppFixture.ResetRole]));
        await simulator.ResetAsync(Ct);

        var deleted = await simulator.ResetAsync(Ct);

        deleted["Trips"].Should().Be(0);
        deleted["Driver reports"].Should().Be(0);
    }

    [Fact]
    public async Task AResetWithoutTheResetRole_IsRefusedWith403_AndDeletesNothing()
    {
        const string driverId = "DRV-5000003";
        await driverApp.ReceiveTripAsync("TR-5000003", driverId, Ct);
        using var driver = driverApp.DriverReportsClient(driverApp.IssueToken([driverId]));
        using var logicApp = driverApp.DriverReportsClient(driverApp.IssueToken([DriverAppFixture.ReportOutcomeRole]));

        using var asDriver = await driver.PostAsync("/v1/reset", content: null, Ct);
        using var asLogicApp = await logicApp.PostAsync("/v1/reset", content: null, Ct);
        using var anonymous = await driverApp.Anonymous.PostAsync("/v1/reset", content: null, Ct);

        asDriver.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        asLogicApp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await driver.GetFromJsonAsync<JsonArray>("/v1/driver/trips", Ct)).Should().ContainSingle();
    }
}
