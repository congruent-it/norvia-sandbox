using System.Net;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Infrastructure.CarrierInbox;
using Norvia.Simulator.Infrastructure.ShipperInbox;
using Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.ShipperInbox;

public sealed class PartnerPortalCarrierInboxTests : IDisposable
{
    private readonly StubHandler _portal = new();
    private readonly HttpClient _http;
    private readonly ShipperInboxOptions _options = new()
    {
        BaseAddress = new Uri("https://portal.example"),
        CarrierEndpointKeys = { ["CAR-0001"] = "carrier-secret" },
    };

    public PartnerPortalCarrierInboxTests() => _http = new HttpClient(_portal);

    public void Dispose()
    {
        _http.Dispose();
        _portal.Dispose();
    }

    private PartnerPortalCarrierInbox Inbox() => new(_http, Options.Create(_options), new FakeLogger<PartnerPortalCarrierInbox>());

    [Fact]
    public async Task AgreedPrices_AsksTheCarriersBookingEndpointWithItsKey_AndGivesWhatNorviaOfferedByCarrierBookingNumber()
    {
        _portal.Answer(HttpStatusCode.OK, """[{"carrierBookingNumber":"CB-1","shipmentNumber":"SH-1","status":"Confirmed","agreedAmount":95.50},{"carrierBookingNumber":"CB-2","agreedAmount":null},{"carrierBookingNumber":"CB-3","agreedAmount":80}]""");

        var prices = await Inbox().AgreedPricesAsync("CAR-0001", TestContext.Current.CancellationToken);

        _portal.Request!.Method.Should().Be(HttpMethod.Get);
        _portal.Request.RequestUri.Should().Be(new Uri("https://portal.example/carrier/v1/CAR-0001/bookings"));
        _portal.Request.Headers.GetValues("X-Api-Key").Should().Equal("carrier-secret");
        prices.Should().BeEquivalentTo(new Dictionary<string, decimal> { ["CB-1"] = 95.50m, ["CB-3"] = 80m });
    }

    [Fact]
    public async Task Outcomes_AsksTheCarriersInvoiceOutcomeEndpoint_AndReadsEachLine()
    {
        _portal.Answer(
            HttpStatusCode.OK,
            """[{"invoiceNo":"VDT-SIM1-INV","correlationId":"corr-1","approvedAmount":190.5,"lines":[{"bookingRef":"VDT-SIM1-3","result":"MATCHED","acceptedAmount":70.5},{"bookingRef":"VDT-SIM1-4","result":"REJECTED","acceptedAmount":0,"remark":"Not agreed"}]}]""");

        var outcomes = await Inbox().OutcomesAsync("CAR-0001", TestContext.Current.CancellationToken);

        _portal.Request!.RequestUri.Should().Be(new Uri("https://portal.example/carrier/v1/CAR-0001/invoice-outcomes"));
        outcomes.Should().BeEquivalentTo([new CarrierInvoiceOutcome(
            "VDT-SIM1-INV", "corr-1", 190.5m, [new CarrierOutcomeLine("VDT-SIM1-3", "MATCHED", 70.5m, null), new CarrierOutcomeLine("VDT-SIM1-4", "REJECTED", 0m, "Not agreed")])], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Outcomes_NothingToldYet_IsEmpty_NotUnreadable()
    {
        _portal.Answer(HttpStatusCode.OK, "[]");

        (await Inbox().OutcomesAsync("CAR-0001", TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task APortalThatRefuses_IsUnreadable(HttpStatusCode status)
    {
        _portal.Answer(status, "");

        (await Inbox().AgreedPricesAsync("CAR-0001", TestContext.Current.CancellationToken)).Should().BeNull();
        (await Inbox().OutcomesAsync("CAR-0001", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task APortalThatCantBeReached_IsUnreadable()
    {
        _portal.Fail(new HttpRequestException("down"));

        (await Inbox().OutcomesAsync("CAR-0001", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task AnAnswerThatIsNotJson_IsUnreadable()
    {
        _portal.Answer(HttpStatusCode.OK, "<html>");

        (await Inbox().OutcomesAsync("CAR-0001", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task ACarrierWithoutAKey_IsUnreadable_WithoutAskingThePortal()
    {
        (await Inbox().AgreedPricesAsync("CAR-0009", TestContext.Current.CancellationToken)).Should().BeNull();

        _portal.Request.Should().BeNull();
    }

    [Fact]
    public async Task WithoutThePortalsAddress_NothingIsAsked()
    {
        _options.BaseAddress = null;

        (await Inbox().OutcomesAsync("CAR-0001", TestContext.Current.CancellationToken)).Should().BeNull();

        _portal.Request.Should().BeNull();
    }
}
