using System.Net;
using System.Net.Http.Json;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// The TMS API is reachable from outside its Container Apps environment, so its queries answer only callers Entra
/// issued a TMS token to, and those are only principals holding one of its roles. The re-rate and the Planner's
/// assignment answer only the TMS UI, which alone holds the manage role.
/// </summary>
public sealed class TransportOrderQueryAuthorizationTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/v1/transport-orders?status=Accepted")]
    [InlineData("/v1/transport-orders/TO-0000001")]
    [InlineData("/v1/transport-orders/00000000-0000-0000-0000-000000000001")]
    [InlineData("/v1/transport-orders/rating-exceptions")]
    [InlineData("/v1/shipments/SH-0000001")]
    public async Task Query_WithoutAToken_IsUnauthorized(string query)
    {
        using var caller = tms.CreateTmsClient(token: null);

        using var response = await caller.GetAsync(new Uri(query, UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Query_TokenForAnotherAudience_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.IntakeRole], audience: "api://norvia-erp"));

        using var response = await caller.GetAsync(new Uri("/v1/transport-orders?status=Accepted", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReRate_WithoutAToken_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(token: null);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders/TO-0000001/re-rate", UriKind.Relative));
        request.Headers.Add("Idempotency-Key", "key-1");
        using var response = await caller.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReRate_TokenForAnotherAudience_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.IntakeRole], audience: "api://norvia-erp"));

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders/TO-0000001/re-rate", UriKind.Relative));
        request.Headers.Add("Idempotency-Key", "key-1");
        using var response = await caller.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReRate_WithTheIntakeRoleOnly_IsForbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders/TO-0000001/re-rate", UriKind.Relative));
        request.Headers.Add("Idempotency-Key", "key-1");
        using var response = await tms.Tms.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TmsFixture.IntakeRole)]
    public async Task Assign_WithoutTheManageRole_IsRefused(string? role)
    {
        using var caller = tms.CreateTmsClient(role is null ? null : tms.IssueToken([role]));

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders/TO-0000001/assign", UriKind.Relative))
        {
            Content = JsonContent.Create(new { assignedTo = "OwnFleet" }),
        };
        request.Headers.Add("Idempotency-Key", "key-1");
        using var response = await caller.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden);
    }
}
