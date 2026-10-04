using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// Only a caller holding the intake role and registered for an intake channel, which in Azure is only the intake Logic
/// App's managed identity, may submit a Transport Order. Anything else is refused before any Transport Order logic
/// runs: nothing is stored and nothing is published, but the refusal is traced under its correlation ID.
/// </summary>
public sealed class TransportOrderIntakeAuthorizationTests(TmsFixture tms)
{
    private const string UnrelatedSigningKey = "an-unrelated-key-that-is-long-enough-for-hmac-sha256";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
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
        using var caller = tms.CreateTmsClient(tms.IssueToken(["TransportOrders.Read"]));

        await ShouldBeRefusedAsync(caller, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Submit_CallerWithoutAnIntakeChannel_IsForbiddenAndLeavesNoTrace()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.IntakeRole], clientId: "some-other-app"));

        await ShouldBeRefusedAsync(caller, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Submit_TokenForAnotherAudience_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.IntakeRole], audience: "api://norvia-erp"));

        await ShouldBeRefusedAsync(caller, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Submit_TokenSignedWithAnotherKey_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.IntakeRole], signingKey: UnrelatedSigningKey));

        await ShouldBeRefusedAsync(caller, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Submit_TokenWithTheIntakeRole_IsAccepted()
    {
        using var response = await SubmitAsync(tms.Tms, Bookings.Valid(NewReference()), Guid.NewGuid().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Submit_Refused_IsTracedAsRefusedUnderItsCorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString();
        using var caller = tms.CreateTmsClient(tms.IssueToken(["TransportOrders.Read"]));

        using var response = await SubmitAsync(caller, Bookings.Valid(NewReference()), correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var refusal = await tms.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Refused", _cancellationToken);
        refusal["flow"].Should().Be("transport-order-intake");
    }

    private async Task ShouldBeRefusedAsync(HttpClient caller, HttpStatusCode refusal)
    {
        var reference = NewReference();
        var correlationId = Guid.NewGuid().ToString();

        using var response = await SubmitAsync(caller, Bookings.Valid(reference), correlationId);

        response.StatusCode.Should().Be(refusal);
        (await ListedReferencesAsync("Accepted")).Should().NotContain(reference);
        (await ListedReferencesAsync("Rejected")).Should().NotContain(reference);
        await OutboxPassedAsync();
        tms.Published.Should().NotContain(message => message.CorrelationId == correlationId);
    }

    // The outbox publishes oldest first, so once a booking accepted after the refusal is published, anything the
    // refusal might have stored would have been published too.
    private async Task OutboxPassedAsync()
    {
        var marker = Guid.NewGuid().ToString();
        using var accepted = await SubmitAsync(tms.Tms, Bookings.Valid(NewReference()), marker);
        accepted.StatusCode.Should().Be(HttpStatusCode.Created);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        timeout.CancelAfter(Timeout);
        while (!tms.Published.Any(message => message.CorrelationId == marker))
        {
            await Task.Delay(TmsFixture.PollInterval, timeout.Token);
        }
    }

    private static string NewReference() => $"BD-{Guid.NewGuid():N}"[..20];

    private async Task<HttpResponseMessage> SubmitAsync(HttpClient caller, JsonObject booking, string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative))
        {
            Content = JsonContent.Create(booking),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
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
