using System.Net;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.ConsigneeTracking.ConsigneeTrackingCalls;

namespace Norvia.Tms.Api.IntegrationTests.ConsigneeTracking;

/// <summary>
/// A code that is malformed, unknown or replaced gets one answer, and only API Management's identity may ask (ADR 0052). The code is
/// never logged: the TMS's logs carry the Transport Order it resolved to, or none.
/// </summary>
public sealed class ConsigneeTrackingRefusalTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task View_AnUnknownCode_AMalformedOne_AndAReplacedOne_AreTheSameAnswer()
    {
        var (number, replaced) = await tms.AcceptedWithCodeAsync(_cancellationToken);
        var current = await tms.ReplacedTrackingCodeOfAsync(number, _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        var answers = new List<(HttpStatusCode Status, string Body)>();
        foreach (var code in new[] { UnknownCode, "too-short", "not a code at all, far too long", replaced })
        {
            using var response = await tms.ViewAsync(tms.ConsigneeTracking, code, _cancellationToken, correlationId);
            answers.Add((response.StatusCode, await response.Content.ReadAsStringAsync(_cancellationToken)));
        }

        answers.Should().AllSatisfy(answer => answer.Status.Should().Be(HttpStatusCode.NotFound));
        answers.Select(answer => answer.Body).Distinct().Should().ContainSingle();
        JsonNode.Parse(answers[0].Body)!["code"]!.GetValue<string>().Should().Be("TrackingCodeNotFound");
        answers[0].Body.Should().NotContain(number).And.NotContain(current).And.NotContain(replaced);
    }

    [Fact]
    public async Task Replace_GivesANewCode_TheOldOneStopsAtOnce_AndTheNewOneOpensTheSameTransportOrder()
    {
        var (number, old) = await tms.AcceptedWithCodeAsync(_cancellationToken);
        (await tms.ViewedAsync(old, _cancellationToken))["transportOrderNumber"]!.GetValue<string>().Should().Be(number);

        var replacement = await tms.ReplacedTrackingCodeOfAsync(number, _cancellationToken);

        replacement.Should().NotBe(old).And.MatchRegex("^[0-9A-HJKMNP-TV-Z]{16}$");
        (await tms.TrackingCodeOfAsync(number, _cancellationToken)).Should().Be(replacement);
        using var oldAnswer = await tms.ViewAsync(tms.ConsigneeTracking, old, _cancellationToken);
        oldAnswer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await tms.ViewedAsync(replacement, _cancellationToken))["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
    }

    [Fact]
    public async Task View_ARejectedBookingsCode_DoesNotExist_SoThereIsNoViewForIt()
    {
        await tms.SubmitRejectedAsync(ShipperTrackingCalls.Shipper, _cancellationToken);

        using var response = await tms.ViewAsync(tms.ConsigneeTracking, UnknownCode, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task View_WithoutAToken_IsUnauthorized()
    {
        var (_, code) = await tms.AcceptedWithCodeAsync(_cancellationToken);
        using var caller = tms.CreateTmsClient(token: null);

        using var response = await tms.ViewAsync(caller, code, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(_cancellationToken)).Should().NotContain(code);
    }

    [Theory]
    [InlineData(TmsFixture.IntakeRole)]
    [InlineData(TmsFixture.ManageRole)]
    [InlineData(TmsFixture.ShipperTrackingReadRole)]
    [InlineData(TmsFixture.TrackingRecordRole)]
    public async Task View_ACallerWithoutTheConsigneeTrackingRole_IsForbidden(string role)
    {
        var (_, code) = await tms.AcceptedWithCodeAsync(_cancellationToken);
        using var caller = tms.CreateTmsClient(tms.IssueToken([role]));

        using var response = await tms.ViewAsync(caller, code, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task View_TheConsigneeTrackingRoleOnATokenForAnotherAudience_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.ConsigneeTrackingReadRole], audience: "api://norvia-erp"));

        using var response = await tms.ViewAsync(caller, UnknownCode, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task View_TheConsigneeTrackingRole_OpensNothingButTheView()
    {
        var (number, _) = await tms.AcceptedWithCodeAsync(_cancellationToken);

        using var transportOrder = await tms.ConsigneeTracking.GetAsync(new Uri($"/v1/transport-orders/{number}", UriKind.Relative), _cancellationToken);
        using var code = await tms.ConsigneeTracking.GetAsync(new Uri($"/v1/transport-orders/{number}/tracking-code", UriKind.Relative), _cancellationToken);

        transportOrder.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        code.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task View_WithoutACodeOrACorrelationId_IsABadRequest()
    {
        using var noCode = await tms.ViewAsync(tms.ConsigneeTracking, trackingCode: null, _cancellationToken);
        using var blankCode = await tms.ViewAsync(tms.ConsigneeTracking, " ", _cancellationToken);
        using var noCorrelation = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/consignee-tracking/view", UriKind.Relative));
        noCorrelation.Headers.Add("X-Tracking-Code", UnknownCode);
        using var noCorrelationAnswer = await tms.ConsigneeTracking.SendAsync(noCorrelation, _cancellationToken);

        noCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        blankCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noCorrelationAnswer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task View_NeverLogsTheCode_AndNamesTheTransportOrderItResolvedToOrNone()
    {
        var (number, code) = await tms.AcceptedWithCodeAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        using var hit = await tms.ViewAsync(tms.ConsigneeTracking, code, _cancellationToken, correlationId);
        using var miss = await tms.ViewAsync(tms.ConsigneeTracking, UnknownCode, _cancellationToken, correlationId);
        hit.StatusCode.Should().Be(HttpStatusCode.OK);
        miss.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await tms.TmsLogAsync(log => log.Text.Contains($"A Consignee opened Transport Order {number}", StringComparison.Ordinal), _cancellationToken);
        await tms.TmsLogAsync(log => log.Text.Contains("matched no Transport Order: none", StringComparison.Ordinal), _cancellationToken);

        tms.TmsLogs.Should().NotContain(log => log.Text.Contains(code, StringComparison.OrdinalIgnoreCase) || log.Text.Contains(UnknownCode, StringComparison.OrdinalIgnoreCase));
    }
}
