using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests;

public sealed class ApiReferenceTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenApiDocument_V1_DescribesTheTransportOrderRoutesAsVersion1()
    {
        using var response = await tms.Tms.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paths = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["paths"]!.AsObject();
        paths.Select(path => path.Key).Should().Contain(["/v1/transport-orders", "/v1/transport-orders/{transportOrderNumber}"]);
    }

    [Fact]
    public async Task OpenApiDocument_V1_HasNoRouteForVehiclesOrDrivers_BecauseTheErpOwnsTheirMasterData()
    {
        using var response = await tms.Tms.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), _cancellationToken);

        var paths = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["paths"]!.AsObject();
        paths.Select(path => path.Key).Should().NotContain(path => path.Contains("vehicle", StringComparison.OrdinalIgnoreCase)
            || path.Contains("driver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Scalar_ServesTheApiReference()
    {
        using var response = await tms.Tms.GetAsync(new Uri("/scalar", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [Fact]
    public void Dashboard_LinksTheTmsApiToScalar()
    {
        tms.TmsDashboardUrls.Should().Contain(url => url.AbsolutePath == "/scalar");
    }
}
