using System.Net;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Infrastructure.ShipperInbox;
using Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.ShipperInbox;

public sealed class PartnerPortalShipperInboxTests : IDisposable
{
    private readonly StubHandler _portal = new();
    private readonly HttpClient _http;
    private readonly ShipperInboxOptions _options = new()
    {
        BaseAddress = new Uri("https://portal.example"),
        ShipperEndpointKeys = { ["SHP-0001"] = "shipper-secret" },
    };

    public PartnerPortalShipperInboxTests() => _http = new HttpClient(_portal);

    public void Dispose()
    {
        _http.Dispose();
        _portal.Dispose();
    }

    private PartnerPortalShipperInbox Inbox() => new(_http, Options.Create(_options), new FakeLogger<PartnerPortalShipperInbox>());

    [Fact]
    public async Task List_AsksTheShippersInvoiceEndpointWithItsKey_AndReadsWhatTheInboxHolds()
    {
        _portal.Answer(HttpStatusCode.OK, """[{"documentNumber":"CN-2026-000001","documentType":"CREDIT_NOTE","correlationId":"corr-2"},{"documentNumber":"INV-2026-000001","documentType":"INVOICE"}]""");

        var documents = await Inbox().ListAsync("SHP-0001", TestContext.Current.CancellationToken);

        _portal.Request!.Method.Should().Be(HttpMethod.Get);
        _portal.Request.RequestUri.Should().Be(new Uri("https://portal.example/shipper/v1/SHP-0001/invoices"));
        _portal.Request.Headers.GetValues("X-Api-Key").Should().Equal("shipper-secret");
        documents.Should().Equal(new InboxDocument("CN-2026-000001", "CREDIT_NOTE", "corr-2"), new InboxDocument("INV-2026-000001", "INVOICE", null));
    }

    [Fact]
    public async Task List_AnEmptyInbox_IsEmpty_NotUnreadable()
    {
        _portal.Answer(HttpStatusCode.OK, "[]");

        (await Inbox().ListAsync("SHP-0001", TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task List_APortalThatRefuses_IsUnreadable(HttpStatusCode status)
    {
        _portal.Answer(status, "");

        (await Inbox().ListAsync("SHP-0001", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task List_APortalThatCantBeReached_IsUnreadable()
    {
        _portal.Fail(new HttpRequestException("down"));

        (await Inbox().ListAsync("SHP-0001", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task List_AnAnswerThatIsNotAList_IsUnreadable()
    {
        _portal.Answer(HttpStatusCode.OK, "<html/>");

        (await Inbox().ListAsync("SHP-0001", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task List_AShipperWithoutAKey_IsUnreadable_WithoutCallingThePortal()
    {
        (await Inbox().ListAsync("SHP-0003", TestContext.Current.CancellationToken)).Should().BeNull();

        _portal.Request.Should().BeNull();
    }

    [Fact]
    public async Task List_WithoutThePortalsAddress_IsUnreadable()
    {
        _options.BaseAddress = null;

        (await Inbox().ListAsync("SHP-0001", TestContext.Current.CancellationToken)).Should().BeNull();
    }
}
