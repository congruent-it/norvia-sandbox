using System.Net;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.CarrierBookings;

/// <summary>
/// Only a caller holding the report role, which in Azure is only the P4 booking Logic App's managed identity, may
/// report a Carrier booking as sent or not delivered; only a caller holding the manage role, the Planner's, may
/// withdraw one. Anything else is refused before any rule runs.
/// </summary>
public sealed class CarrierBookingReportAndWithdrawAuthorizationTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReportSent_WithoutAToken_IsUnauthorized()
    {
        var carrierBookingNumber = await ReleasedAsync();
        using var caller = tms.CreateTmsClient(token: null);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/report-sent", UriKind.Relative));
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        var response = await caller.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReportSent_TokenWithoutTheReportRole_IsForbidden()
    {
        var carrierBookingNumber = await ReleasedAsync();
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.ManageRole]));

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/report-sent", UriKind.Relative));
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        var response = await caller.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReportSent_TokenWithTheReportRole_IsAccepted()
    {
        var carrierBookingNumber = await ReleasedAsync();

        using var response = await tms.ReportSentAsync(carrierBookingNumber, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Withdraw_WithoutAToken_IsUnauthorized()
    {
        var carrierBookingNumber = await ReleasedAsync();
        using var caller = tms.CreateTmsClient(token: null);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/withdraw", UriKind.Relative));
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await caller.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Withdraw_TokenWithoutTheManageRole_IsForbidden()
    {
        var carrierBookingNumber = await ReleasedAsync();
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.AnswerRole]));

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/withdraw", UriKind.Relative));
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await caller.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Withdraw_TokenWithTheManageRole_IsAccepted()
    {
        var carrierBookingNumber = await ReleasedAsync();

        using var response = await tms.WithdrawAsync(carrierBookingNumber, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A Carrier Shipment the Planner released, with its Requested Carrier booking.</summary>
    private async Task<string> ReleasedAsync()
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);
        var released = await tms.ReleasedAsync(assigned["shipmentNumber"]!.GetValue<string>(), _cancellationToken);
        await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", _cancellationToken);
        return released["carrierBookingNumber"]!.GetValue<string>();
    }
}
