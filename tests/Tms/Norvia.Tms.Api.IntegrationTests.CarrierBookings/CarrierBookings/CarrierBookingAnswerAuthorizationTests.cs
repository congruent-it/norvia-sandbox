using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.CarrierBookings;

/// <summary>
/// Only a caller holding the answer role, which in Azure is only the Carrier-answer Logic App's managed identity, may
/// record a Carrier's answer, for every answer operation. Anything else is refused before any answer rule runs:
/// nothing is stored and nothing is published.
/// </summary>
public sealed class CarrierBookingAnswerAuthorizationTests(TmsFixture tms)
{
    private static JsonObject Confirmation => new()
    {
        ["carrierId"] = PlanningCalls.CarrierId,
        ["outcome"] = "Confirmed",
        ["carrierBookingReference"] = CarrierBookingCalls.NewReference(),
    };

    private static readonly JsonObject Rejection = new() { ["carrierId"] = PlanningCalls.CarrierId, ["outcome"] = "Rejected", ["reasonCode"] = "NoCapacity" };

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Confirm_WithoutAToken_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(token: null);

        var response = await AnswerAsync(caller, Confirmation);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirm_TokenWithoutTheAnswerRole_IsForbidden()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.ManageRole]));

        var response = await AnswerAsync(caller, Confirmation);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Confirm_TokenWithTheAnswerRole_IsAccepted()
    {
        var response = await AnswerAsync(tms.CarrierAnswer, Confirmation);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reject_WithoutAToken_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(token: null);

        var response = await AnswerAsync(caller, Rejection);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reject_TokenWithoutTheAnswerRole_IsForbidden()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.ManageRole]));

        var response = await AnswerAsync(caller, Rejection);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reject_TokenWithTheAnswerRole_IsAccepted()
    {
        var response = await AnswerAsync(tms.CarrierAnswer, Rejection);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> AnswerAsync(HttpClient caller, JsonObject answer)
    {
        var number = await tms.SubmitAcceptedAsync(_cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);
        var released = await tms.ReleasedAsync(assigned["shipmentNumber"]!.GetValue<string>(), _cancellationToken);
        var carrierBookingNumber = released["carrierBookingNumber"]!.GetValue<string>();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/answer", UriKind.Relative))
        {
            Content = JsonContent.Create(answer),
        };
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        return await caller.SendAsync(request, _cancellationToken);
    }
}
