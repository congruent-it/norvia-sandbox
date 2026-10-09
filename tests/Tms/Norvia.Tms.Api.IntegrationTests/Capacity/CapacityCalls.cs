using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.Capacity;

/// <summary>What the Planning horizon tests do at the TMS's boundary: read the open days, and edit a Shift as the Planner does through the TMS UI.</summary>
internal static class CapacityCalls
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>The day it is now in Norvia's time zone.</summary>
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Amsterdam).DateTime);

    /// <summary>A moment 00:10 into the day, in Norvia's time zone: just after the horizon job's midnight.</summary>
    public static DateTimeOffset ShortlyAfterMidnightOf(DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(0, 10));
        return new DateTimeOffset(local, Amsterdam.GetUtcOffset(local));
    }

    /// <summary>The open days, earliest first, from the query API.</summary>
    public static async Task<IReadOnlyList<JsonObject>> ListOpenDaysAsync(this TmsFixture tms, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var response = await tms.TmsUi.GetAsync(new Uri("/v1/capacity/days", UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return [.. JsonNode.Parse(body)!.AsArray().Select(day => day!.AsObject())];
    }

    /// <summary>Waits until the open days start at the date and run on for the whole Planning horizon, and returns them.</summary>
    public static async Task<IReadOnlyList<JsonObject>> HorizonStartingAtAsync(this TmsFixture tms, DateOnly first, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            var days = await tms.ListOpenDaysAsync(timeout.Token);
            var horizon = days.Where(day => DateOf(day) >= first && DateOf(day) <= first.AddDays(14)).ToList();
            if (horizon.Count == 15 && days.All(day => DateOf(day) >= first || DateOf(day) > first.AddDays(14)))
            {
                return horizon;
            }

            await Task.Delay(TmsFixture.PollInterval, timeout.Token);
        }
    }

    public static DateOnly DateOf(JsonObject day) => DateOnly.Parse(day["date"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The Planner sets one Driver's Shift on a day.</summary>
    public static async Task<HttpResponseMessage> EditShiftAsync(
        this TmsFixture tms, DateOnly day, string driverId, string start, string end, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        return await tms.TmsUi.PutAsJsonAsync(
            new Uri($"/v1/capacity/days/{day:yyyy-MM-dd}/shifts/{driverId}", UriKind.Relative), new { start, end }, cancellationToken);
    }
}
