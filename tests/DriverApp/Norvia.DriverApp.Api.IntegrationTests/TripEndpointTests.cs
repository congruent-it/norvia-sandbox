using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.DriverApp.Api.IntegrationTests;

// Serial with the tests of the reporting path: the Driver app's "unavailable" fault is for the whole app.
[Collection("Driver app")]
public sealed class TripEndpointTests(DriverAppFixture driverApp)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task APostedTripVersion_IsShownToItsDriver_WithItsStopsInOrder()
    {
        var response = await PostAsync(await DriverAppFixture.SampleAsync("two-shipments", "TR-1000001", version: 1, driverId: "DRV-1000001"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var trips = await TripsOfAsync("DRV-1000001");
        var trip = trips.Should().ContainSingle().Subject;
        trip.GetProperty("tripNumber").GetString().Should().Be("TR-1000001");
        trip.GetProperty("version").GetInt32().Should().Be(1);
        trip.GetProperty("stops").EnumerateArray().Select(stop => stop.GetProperty("sequence").GetInt32()).Should().Equal(1, 2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-credential")]
    public async Task APostedTripVersion_WithoutTheCredential_IsRefusedWith401_AndStoresNothing(string? apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trips")
        {
            Content = new StringContent(await DriverAppFixture.SampleAsync("two-shipments", "TR-1000012", version: 1, driverId: "DRV-1000012"), Encoding.UTF8, "application/json"),
        };
        if (apiKey is not null)
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        var response = await driverApp.Anonymous.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await TripsOfAsync("DRV-1000012")).Should().BeEmpty();
    }

    [Fact]
    public async Task ATripVersion_IsNotShownToAnotherDriver()
    {
        await PostAsync(await DriverAppFixture.SampleAsync("two-shipments", "TR-1000002", version: 1, driverId: "DRV-1000002"));

        (await TripsOfAsync("DRV-1000003")).Should().BeEmpty();
    }

    [Fact]
    public async Task AHigherVersion_ReplacesTheStoredOne()
    {
        await PostAsync(await DriverAppFixture.SampleAsync("two-shipments", "TR-1000003", version: 1, driverId: "DRV-1000004"));

        var response = await PostAsync(await DriverAppFixture.SampleAsync("without-stops", "TR-1000003", version: 2, driverId: "DRV-1000004"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var trip = (await TripsOfAsync("DRV-1000004")).Should().ContainSingle().Subject;
        trip.GetProperty("version").GetInt32().Should().Be(2);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task AnEqualOrLowerVersion_IsAcknowledgedAndIgnored(int version)
    {
        var tripNumber = $"TR-200000{version}";
        await PostAsync(await DriverAppFixture.SampleAsync("two-shipments", tripNumber, version: 2, driverId: $"DRV-200000{version}"));

        var response = await PostAsync(await DriverAppFixture.SampleAsync("without-stops", tripNumber, version: version, driverId: $"DRV-200000{version}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var trip = (await TripsOfAsync($"DRV-200000{version}")).Should().ContainSingle().Subject;
        trip.GetProperty("version").GetInt32().Should().Be(2);
        trip.GetProperty("stops").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task AVersionWithoutStops_IsShownWithNoStops()
    {
        await PostAsync(await DriverAppFixture.SampleAsync("without-stops", "TR-1000004", version: 2, driverId: "DRV-1000005"));

        var trip = (await TripsOfAsync("DRV-1000005")).Should().ContainSingle().Subject;
        trip.GetProperty("version").GetInt32().Should().Be(2);
        trip.GetProperty("stops").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task WhileTheDriverAppIsUnavailable_TheEndpointReturns503_AndStoresNothing()
    {
        await SetUnavailableAsync(true);
        try
        {
            var response = await PostAsync(await DriverAppFixture.SampleAsync("two-shipments", "TR-1000005", version: 1, driverId: "DRV-1000006"));

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            await SetUnavailableAsync(false);
        }

        (await TripsOfAsync("DRV-1000006")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("version", 0)]
    [InlineData("tripNumber", "TR-12")]
    [InlineData("plannedDrivingDuration", "six hours")]
    [InlineData("plannedDrivingDuration", "PT0S")]
    [InlineData("plannedDrivingDuration", "PT25H")]
    public async Task AMalformedVersion_IsRefusedWith400_NamingTheField_AndStoresNothing(string field, object value)
    {
        var json = await DriverAppFixture.SampleAsync("two-shipments", "TR-1000006", version: 1, driverId: "DRV-1000007", trip =>
            trip[field] = value is int number ? JsonValue.Create(number) : JsonValue.Create(value.ToString()));

        var response = await PostAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain(field);
        (await TripsOfAsync("DRV-1000007")).Should().BeEmpty();
    }

    [Fact]
    public async Task ABlankDriver_IsRefusedWith400_NotFailedWith500()
    {
        var json = await DriverAppFixture.SampleAsync("two-shipments", "TR-1000007", version: 1, driverId: "");

        var response = await PostAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ABodyOutsideTheCanonicalModel_IsRefusedWith400()
    {
        var json = await DriverAppFixture.SampleAsync("two-shipments", "TR-1000008", version: 1, driverId: "DRV-1000008", trip =>
            trip["stops"]![0]!["kind"] = "Teleport");

        var response = await PostAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhileTheDriverAppIsUnavailable_AMalformedVersionIsStillRefusedWith503()
    {
        await SetUnavailableAsync(true);
        try
        {
            var json = await DriverAppFixture.SampleAsync("two-shipments", "TR-1000009", version: 0, driverId: "DRV-1000009");

            var response = await PostAsync(json);

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            await SetUnavailableAsync(false);
        }
    }

    [Fact]
    public async Task TheSameVersionSentManyTimesAtOnce_IsAcknowledgedEveryTime_AndStoredOnce()
    {
        var json = await DriverAppFixture.SampleAsync("two-shipments", "TR-1000010", version: 1, driverId: "DRV-1000010");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostAsync(json)));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        (await TripsOfAsync("DRV-1000010")).Should().ContainSingle();
    }

    [Fact]
    public async Task AVersionAndALowerOne_SentAtOnce_LeaveTheHigherOneStored()
    {
        var versions = await Task.WhenAll(Enumerable.Range(1, 6)
            .Select(version => DriverAppFixture.SampleAsync("two-shipments", "TR-1000011", version, driverId: "DRV-1000011")));

        var responses = await Task.WhenAll(versions.Select(PostAsync));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        var trip = (await TripsOfAsync("DRV-1000011")).Should().ContainSingle().Subject;
        trip.GetProperty("version").GetInt32().Should().Be(6);
    }

    [Fact]
    public async Task TheUnavailableSetting_CanBeReadBack()
    {
        await SetUnavailableAsync(true);
        try
        {
            var on = await driverApp.DriverApp.GetFromJsonAsync<JsonElement>("/v1/settings/unavailable", Ct);
            on.GetProperty("unavailable").GetBoolean().Should().BeTrue();
        }
        finally
        {
            await SetUnavailableAsync(false);
        }

        var off = await driverApp.DriverApp.GetFromJsonAsync<JsonElement>("/v1/settings/unavailable", Ct);
        off.GetProperty("unavailable").GetBoolean().Should().BeFalse();
    }

    private async Task SetUnavailableAsync(bool unavailable)
    {
        var response = await driverApp.DriverApp.PutAsJsonAsync("/v1/settings/unavailable", new { unavailable }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private Task<HttpResponseMessage> PostAsync(string json) =>
        driverApp.DriverApp.PostAsync("/v1/trips", new StringContent(json, Encoding.UTF8, "application/json"), Ct);

    private async Task<List<JsonElement>> TripsOfAsync(string driverId) =>
        (await driverApp.DriverApp.GetFromJsonAsync<List<JsonElement>>($"/v1/drivers/{driverId}/trips", Ct))!;
}
