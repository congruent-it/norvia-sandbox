using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.Erp.Api.IntegrationTests.Reset;

/// <summary>An ERP whose reset is switched off, which is how every ERP but a local one starts (ADR 0039).</summary>
public sealed class ErpResetSwitchedOffTests(ErpFixture fixture) : ResetTest(fixture)
{
    [Fact]
    public async Task AResetWithTheResetRole_IsRefusedWith403AndTheResetDisabledCode_AndDeletesNothing()
    {
        await RecordOneOfEveryKindAsync();
        var held = await HeldAsync();
        using var simulator = Fixture.ResetOffApiAs(ErpFixture.ResetRole);

        using var response = await simulator.PostAsync(new Uri("/v1/reset", UriKind.Relative), content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOfAsync(response)).Should().Be("ResetDisabled");
        (await HeldAsync()).Should().Be(held);
    }

    [Fact]
    public async Task TheReadiness_SaysTheResetIsSwitchedOff_AndLooksAtNoSubscription()
    {
        Fixture.Subscriptions.Hold("transport-order-events/billing");
        using var simulator = Fixture.ResetOffApiAs(ErpFixture.ResetRole);

        var readiness = await simulator.GetFromJsonAsync<JsonObject>(new Uri("/v1/reset", UriKind.Relative), Ct);

        readiness!["enabled"]!.GetValue<bool>().Should().BeFalse();
        readiness["busySubscription"].Should().BeNull();
    }
}
