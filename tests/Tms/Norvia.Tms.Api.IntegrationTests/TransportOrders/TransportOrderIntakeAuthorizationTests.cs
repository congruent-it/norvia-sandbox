using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.AppHost;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// Only a caller holding the intake role, which in Azure is only the intake Logic App's managed identity, may submit
/// a Transport Order. Anything else is refused before any Transport Order logic runs: nothing is stored and nothing
/// is published.
/// </summary>
public sealed class TransportOrderIntakeAuthorizationTests(TmsFixture tms)
{
    private static readonly TimeSpan OutboxSettleTime = TimeSpan.FromSeconds(3);
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submit_WithoutAToken_IsUnauthorizedAndLeavesNoTrace()
    {
        using var caller = tms.CreateTmsClient(token: null);

        await ShouldBeRefusedAsync(caller, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Submit_TokenWithoutTheIntakeRole_IsForbiddenAndLeavesNoTrace()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken("TransportOrders.Read"));

        await ShouldBeRefusedAsync(caller, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Submit_TokenForAnotherAudience_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(tms.IssueTokenFor("api://norvia-erp", TmsFixture.IntakeRole));

        await ShouldBeRefusedAsync(caller, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Submit_TokenSignedWithAnotherKey_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(
            TmsFixture.IssueToken(LocalTokens.TmsAudience, "an-unrelated-key-that-is-long-enough-for-hmac-sha256", TmsFixture.IntakeRole));

        await ShouldBeRefusedAsync(caller, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Submit_TokenWithTheIntakeRole_IsAccepted()
    {
        using var response = await SubmitAsync(tms.Tms, Bookings.Valid(NewReference()), Guid.NewGuid().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task ShouldBeRefusedAsync(HttpClient caller, HttpStatusCode refusal)
    {
        var reference = NewReference();
        var correlationId = Guid.NewGuid().ToString();

        using var response = await SubmitAsync(caller, Bookings.Valid(reference), correlationId);

        response.StatusCode.Should().Be(refusal);
        (await ListedReferencesAsync("Accepted")).Should().NotContain(reference);
        (await ListedReferencesAsync("Rejected")).Should().NotContain(reference);
        await Task.Delay(OutboxSettleTime, _cancellationToken);
        tms.Published.Should().NotContain(message => message.CorrelationId == correlationId);
    }

    private static string NewReference() => $"BD-{Guid.NewGuid():N}"[..20];

    private async Task<HttpResponseMessage> SubmitAsync(HttpClient caller, JsonObject booking, string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative))
        {
            Content = JsonContent.Create(booking),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
        request.Headers.Add("X-Intake-Channel", "ShipperApi");
        return await caller.SendAsync(request, _cancellationToken);
    }

    private async Task<IEnumerable<string?>> ListedReferencesAsync(string status)
    {
        using var response = await tms.Tms.GetAsync(new Uri($"/v1/transport-orders?status={status}", UriKind.Relative), _cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!.AsArray();
        return listed.Select(order => order!["shipperReference"]?.GetValue<string>());
    }
}
