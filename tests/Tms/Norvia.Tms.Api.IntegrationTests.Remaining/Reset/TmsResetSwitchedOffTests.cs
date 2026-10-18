using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.Reset;

/// <summary>A TMS whose reset is switched off, which is how every TMS but a local one starts (ADR 0039).</summary>
public sealed class TmsResetSwitchedOffTests(ResetOffTmsFixture tms) : IClassFixture<ResetOffTmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AResetWithTheResetRole_IsRefusedWith403AndTheResetDisabledCode_AndDeletesNothing()
    {
        var orderNumber = await tms.SubmitAcceptedAsync(_cancellationToken);
        using var simulator = tms.CreateTmsClient(tms.IssueToken([ResetTmsFixture.ResetRole]));

        using var response = await simulator.ResetOnceAsync(_cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["code"]!.GetValue<string>().Should().Be("ResetDisabled");
        (await tms.Tms.StatusOfAsync($"/v1/transport-orders/{orderNumber}", _cancellationToken)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheReadiness_SaysTheResetIsSwitchedOff()
    {
        using var simulator = tms.CreateTmsClient(tms.IssueToken([ResetTmsFixture.ResetRole]));

        using var response = await simulator.GetAsync(new Uri("/v1/reset", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["enabled"]!.GetValue<bool>().Should().BeFalse();
    }
}
