using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Erp.Application.MasterData.RateCards;
using Norvia.Erp.Web.ErpApi;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests.ErpApi;

public sealed class MasterDataApiClientTests : IDisposable
{
    private readonly StubHandler _erpApi = new();
    private readonly IErpApiTokens _tokens = Substitute.For<IErpApiTokens>();
    private readonly FakeLogger<MasterDataApiClient> _logger = new();
    private readonly ErpActivities _activities;
    private readonly HttpClient _http;
    private readonly MasterDataApiClient _client;

    public MasterDataApiClientTests()
    {
        var environment = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        environment.ApplicationName.Returns("Norvia.Erp.Web.Tests");
        _activities = new ErpActivities(environment);
        _http = new HttpClient(_erpApi) { BaseAddress = new Uri("http://erp-api.example") };
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("erp-ui-token");
        _client = new MasterDataApiClient(_http, _tokens, _activities, _logger);
    }

    public void Dispose()
    {
        _activities.Dispose();
        _http.Dispose();
        _erpApi.Dispose();
    }

    [Fact]
    public async Task SaveShipperAsync_PutsTheShipper_AsTheErpUi_AndSaysItWasCreated()
    {
        _erpApi.Answer(HttpStatusCode.Created, """{"shipper":{"id":"SHP-0009","name":"Nieuw","active":true,"revision":1},"outcome":"Created"}""");

        var answer = await _client.SaveShipperAsync("SHP-0009", "Nieuw", active: true, Xunit.TestContext.Current.CancellationToken);

        answer.Should().Be(new MasterDataAnswer.Saved(MasterDataOutcome.Created, 1));
        _erpApi.Request!.Method.Should().Be(HttpMethod.Put);
        _erpApi.Request.RequestUri.Should().Be(new Uri("http://erp-api.example/v1/shippers/SHP-0009"));
        _erpApi.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        _erpApi.Request.Headers.Authorization.Parameter.Should().Be("erp-ui-token");
        var body = JsonNode.Parse(_erpApi.RequestBody!)!;
        body["name"]!.GetValue<string>().Should().Be("Nieuw");
        body["active"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task SaveCarrierAsync_PutsTheCarrier_AndSaysUnchangedWhenItAlreadyWasThat()
    {
        _erpApi.Answer(HttpStatusCode.OK, """{"carrier":{"id":"CAR-0001","name":"Van Dijk","active":false,"revision":4},"outcome":"Unchanged"}""");

        var answer = await _client.SaveCarrierAsync("CAR-0001", "Van Dijk", active: false, Xunit.TestContext.Current.CancellationToken);

        answer.Should().Be(new MasterDataAnswer.Saved(MasterDataOutcome.Unchanged, 4));
        _erpApi.Request!.RequestUri!.AbsolutePath.Should().Be("/v1/carriers/CAR-0001");
        JsonNode.Parse(_erpApi.RequestBody!)!["active"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task SaveVehicleAsync_SendsTheRegistrationTypePayloadAndPalletPlaces_AndSaysUpdated()
    {
        _erpApi.Answer(HttpStatusCode.OK, """{"vehicle":{"id":"VEH-1","revision":2},"outcome":"Updated"}""");

        var answer = await _client.SaveVehicleAsync("VEH-1", "AB-123-C", "RigidTruck", 9000, 18, active: true, Xunit.TestContext.Current.CancellationToken);

        answer.Should().Be(new MasterDataAnswer.Saved(MasterDataOutcome.Updated, 2));
        _erpApi.Request!.RequestUri!.AbsolutePath.Should().Be("/v1/vehicles/VEH-1");
        var body = JsonNode.Parse(_erpApi.RequestBody!)!;
        body["registration"]!.GetValue<string>().Should().Be("AB-123-C");
        body["type"]!.GetValue<string>().Should().Be("RigidTruck");
        body["payloadKg"]!.GetValue<int>().Should().Be(9000);
        body["palletPlaces"]!.GetValue<int>().Should().Be(18);
    }

    [Fact]
    public async Task SaveDriverAsync_SendsTheLicences()
    {
        _erpApi.Answer(HttpStatusCode.Created, """{"driver":{"id":"DRV-1","revision":1},"outcome":"Created"}""");

        await _client.SaveDriverAsync("DRV-1", "Jan", ["B", "CE"], active: true, Xunit.TestContext.Current.CancellationToken);

        _erpApi.Request!.RequestUri!.AbsolutePath.Should().Be("/v1/drivers/DRV-1");
        JsonNode.Parse(_erpApi.RequestBody!)!["licences"]!.AsArray().Select(licence => licence!.GetValue<string>()).Should().Equal("B", "CE");
    }

    [Fact]
    public async Task SaveRateCardAsync_SendsTheShipperAndTheWholeListOfVersions()
    {
        _erpApi.Answer(HttpStatusCode.Created, """{"rateCard":{"id":"RC-1","shipperId":"SHP-1","revision":1,"versions":[]},"outcome":"Created"}""");
        var version = new RateCardVersionData(1, new DateOnly(2026, 1, 1), null, [new ZoneData("NL", "NL", ["30"])], []);

        var answer = await _client.SaveRateCardAsync("RC-1", "SHP-1", [version], Xunit.TestContext.Current.CancellationToken);

        answer.Should().Be(new MasterDataAnswer.Saved(MasterDataOutcome.Created, 1));
        _erpApi.Request!.RequestUri!.AbsolutePath.Should().Be("/v1/rate-cards/RC-1");
        var body = JsonNode.Parse(_erpApi.RequestBody!)!;
        body["shipperId"]!.GetValue<string>().Should().Be("SHP-1");
        body["versions"]![0]!["validFrom"]!.GetValue<string>().Should().Be("2026-01-01");
        body["versions"]![0]!["zones"]![0]!["postcodePrefixes"]![0]!.GetValue<string>().Should().Be("30");
    }

    [Fact]
    public async Task SaveCarrierRateCardAsync_SendsTheCarrier()
    {
        _erpApi.Answer(HttpStatusCode.Created, """{"carrierRateCard":{"id":"CRC-1","carrierId":"CAR-1","revision":1,"versions":[]},"outcome":"Created"}""");

        await _client.SaveCarrierRateCardAsync("CRC-1", "CAR-1", [], Xunit.TestContext.Current.CancellationToken);

        _erpApi.Request!.RequestUri!.AbsolutePath.Should().Be("/v1/carrier-rate-cards/CRC-1");
        JsonNode.Parse(_erpApi.RequestBody!)!["carrierId"]!.GetValue<string>().Should().Be("CAR-1");
    }

    [Fact]
    public async Task SaveShipperAsync_ReturnsTheRefusalsMessage_WhenTheErpApiRefuses()
    {
        _erpApi.Answer(HttpStatusCode.BadRequest, """{"title":"The Shipper can't be saved.","detail":"The Shipper needs an ID and a name.","code":"MasterDataFieldRequired"}""", "application/problem+json");

        var answer = await _client.SaveShipperAsync("SHP-1", "", active: true, Xunit.TestContext.Current.CancellationToken);

        answer.Should().Be(new MasterDataAnswer.Refused("The Shipper needs an ID and a name."));
        _logger.Collector.GetSnapshot().Should().ContainSingle(record => record.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task SaveShipperAsync_Throws_WhenTheErpApiFails()
    {
        _erpApi.Answer(HttpStatusCode.InternalServerError, "{}");

        var save = () => _client.SaveShipperAsync("SHP-1", "Name", active: true, Xunit.TestContext.Current.CancellationToken);

        await save.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SaveShipperAsync_Throws_WhenTheErpApiAnswersASuccessWithoutARevision()
    {
        _erpApi.Answer(HttpStatusCode.OK, """{"outcome":"Updated"}""");

        var save = () => _client.SaveShipperAsync("SHP-1", "Name", active: true, Xunit.TestContext.Current.CancellationToken);

        await save.Should().ThrowAsync<HttpRequestException>();
    }
}
