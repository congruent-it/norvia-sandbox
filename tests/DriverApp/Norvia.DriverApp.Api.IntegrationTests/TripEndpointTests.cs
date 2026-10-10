using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Norvia.DriverApp.Api.IntegrationTests;

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
