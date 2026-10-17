using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.DriverApp.Api.IntegrationTests.Reset;

/// <summary>A Driver app whose reset is switched off, which is how every Driver app but a local one starts (ADR 0039).</summary>
public sealed class DriverAppResetSwitchedOffTests(ResetOffDriverAppFixture driverApp) : IClassFixture<ResetOffDriverAppFixture>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AResetWithTheResetRole_IsRefusedWith403AndTheResetDisabledCode_AndDeletesNothing()
    {
        const string driverId = "DRV-5000004";
        await driverApp.ReceiveTripAsync("TR-5000004", driverId, Ct);
        using var driver = driverApp.DriverReportsClient(driverApp.IssueToken([driverId]));
        using var simulator = driverApp.DriverReportsClient(driverApp.IssueToken([DriverAppFixture.ResetRole]));

        using var response = await simulator.PostAsync("/v1/reset", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["code"]!.GetValue<string>().Should().Be("ResetDisabled");
        (await driver.GetFromJsonAsync<JsonArray>("/v1/driver/trips", Ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task TheReadiness_SaysTheResetIsSwitchedOff()
    {
        using var simulator = driverApp.DriverReportsClient(driverApp.IssueToken([DriverAppFixture.ResetRole]));

        using var response = await simulator.GetAsync("/v1/reset", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["enabled"]!.GetValue<bool>().Should().BeFalse();
    }
}
