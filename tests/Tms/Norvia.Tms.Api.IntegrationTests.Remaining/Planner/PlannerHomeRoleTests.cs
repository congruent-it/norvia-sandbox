using System.Net;

namespace Norvia.Tms.Api.IntegrationTests.Planner;

/// <summary>The TMS UI opens on the home page of the role the browser last switched to: the Planner's own, and Customer service's first view.</summary>
public sealed class PlannerHomeRoleTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null, "/home")]
    [InlineData("planner", "/home")]
    [InlineData("customer-service", "/rejected-bookings")]
    public async Task Root_OpensTheHomeOfTheRole(string? role, string expected)
    {
        using var web = await tms.CreateTmsWebClientAsync(_cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        if (role is not null)
        {
            request.Headers.Add("Cookie", $"norvia-tms-role={role}");
        }

        using var response = await web.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(expected);
    }
}
