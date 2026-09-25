using System.Net;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// The TMS API is reachable from outside its Container Apps environment, so its queries answer only callers Entra
/// issued a TMS token to, and those are only principals holding one of its roles.
/// </summary>
public sealed class TransportOrderQueryAuthorizationTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/v1/transport-orders?status=Accepted")]
    [InlineData("/v1/transport-orders/TO-0000001")]
    [InlineData("/v1/transport-orders/00000000-0000-0000-0000-000000000001")]
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
}
