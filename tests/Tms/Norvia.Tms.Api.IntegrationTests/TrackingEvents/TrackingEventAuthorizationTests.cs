using System.Net;
using static Norvia.Tms.Api.IntegrationTests.TrackingEvents.TrackingEventCalls;

namespace Norvia.Tms.Api.IntegrationTests.TrackingEvents;

/// <summary>
/// Only a caller holding the record role, which in Azure is only the Carrier-tracking Logic App's managed identity (and
/// later P12's Driver reports Logic App's), may record a Tracking event. Anything else is refused before any rule runs:
/// nothing is stored and nothing is published.
/// </summary>
public sealed class TrackingEventAuthorizationTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Record_WithoutAToken_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(token: null);

        using var response = await tms.RecordAsync(caller, Collected("VDT-1", DateTimeOffset.UtcNow), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(TmsFixture.IntakeRole)]
    [InlineData(TmsFixture.ManageRole)]
    [InlineData(TmsFixture.AnswerRole)]
    [InlineData(TmsFixture.ReportRole)]
    public async Task Record_TokenWithAnotherRole_IsForbidden(string role)
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([role]));

        using var response = await tms.RecordAsync(caller, Collected("VDT-1", DateTimeOffset.UtcNow), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Record_TokenWithTheRecordRole_IsAllowedPastAuthorization()
    {
        using var response = await tms.RecordAsync(Collected("NO-SUCH-REFERENCE", DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the caller is allowed in and the TMS refuses the reference");
    }

    [Fact]
    public async Task Record_TokenSignedWithAnotherKey_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.TrackingRecordRole], signingKey: new string('a', 64)));

        using var response = await tms.RecordAsync(caller, Collected("VDT-1", DateTimeOffset.UtcNow), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
