using System.Net;
using static Norvia.Tms.Api.IntegrationTests.ShipperTracking.ShipperTrackingCalls;

namespace Norvia.Tms.Api.IntegrationTests.ShipperTracking;

/// <summary>
/// Only API Management holds <c>ShipperTracking.Read</c>, and the TMS trusts the Shipper ID only from that caller, so no other
/// caller can read a Shipper's Transport Orders, however it got a TMS token (P8).
/// </summary>
public sealed class ShipperTrackingAuthorizationTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_WithoutAToken_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(token: null);

        using var response = await tms.ListAsync(caller, Shipper, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(TmsFixture.IntakeRole)]
    [InlineData(TmsFixture.ManageRole)]
    [InlineData(TmsFixture.TrackingRecordRole)]
    public async Task List_ACallerWithoutTheShipperTrackingRole_IsForbidden(string role)
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([role]));

        using var response = await tms.ListAsync(caller, Shipper, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_ATokenWithNoRoles_IsForbidden()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([]));

        using var response = await tms.ListAsync(caller, Shipper, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_TheShipperTrackingRoleOnATokenForAnotherAudience_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.ShipperTrackingReadRole], audience: "api://norvia-erp"));

        using var response = await tms.ListAsync(caller, Shipper, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithoutAShipperId_IsABadRequest()
    {
        using var response = await tms.ListAsync(tms.ShipperTracking, shipperId: null, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_AShipperTheTmsDoesNotKnow_ListsNothing()
    {
        (await tms.ListedForAsync("SHP-9999", _cancellationToken)).Should().BeEmpty();
    }
}
