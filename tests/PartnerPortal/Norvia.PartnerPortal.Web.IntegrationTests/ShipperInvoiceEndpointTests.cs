using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Web.IntegrationTests;

/// <summary>
/// A Shipper Persona's invoice endpoint and invoices inbox at the portal's boundary (E1, user stories 8 and 9): the endpoint only acknowledges, a document is stored
/// once per document number, and the inbox shows it, newest first, to its own Shipper only. Each test sends documents of numbers of its own, so none depends on the others.
/// </summary>
public sealed class ShipperInvoiceEndpointTests(PartnerPortalFixture fixture)
{
    private const string Shipper = "brouwer-dranken";
    private const string ShipperPartyId = "SHP-0001";
    private const string OtherShipper = "tuincentrum-groenrijk";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnInvoice_IsAcknowledgedAndShownInTheShippersInbox_WithItsShipperReferencesAndTotals()
    {
        var number = NewNumber("INV");

        using var response = await SendAsync(InvoiceBody(number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["repeated"]!.GetValue<bool>().Should().BeFalse();
        var shown = (await InboxAsync(Shipper)).Should().ContainSingle(invoice => invoice.DocumentNumber == number).Subject;
        shown.DocumentType.Should().Be(InvoiceDocumentType.Invoice);
        shown.DocumentDate.Should().Be(new DateOnly(2026, 10, 19));
        shown.Credit.Should().BeNull();
        shown.CorrelationId.Should().Be("corr-" + number);
        shown.TotalWithoutVat.Should().Be(150m);
        shown.Vat.Should().Be(31.50m);
        shown.TotalWithVat.Should().Be(181.50m);
        var line = shown.Lines.Should().ContainSingle().Subject;
        line.TransportOrderNumber.Should().Be("TO-0000042");
        line.BookingRef.Should().Be("BD-2026-1187");
        line.DeliveryDate.Should().Be(new DateOnly(2026, 10, 16));
        line.LineTotal.Should().Be(150m);
        line.ChargeLines.Select(charge => (charge.Code, charge.Amount)).Should().Equal((ChargeCode.Freight, 96.40m), (ChargeCode.MinimumChargeAdjustment, 53.60m));
    }

    [Fact]
    public async Task ADocumentThatRepeatsOneTheInboxHas_IsAcknowledgedAsARepeat_AndStoredOnce()
    {
        var number = NewNumber("INV");
        using var first = await SendAsync(InvoiceBody(number));

        using var repeat = await SendAsync(InvoiceBody(number));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        (await repeat.Content.ReadFromJsonAsync<JsonObject>(Ct))!["repeated"]!.GetValue<bool>().Should().BeTrue();
        (await InboxAsync(Shipper)).Where(invoice => invoice.DocumentNumber == number).Should().ContainSingle();
    }

    [Fact]
    public async Task TheSameDocumentTwiceAtOnce_IsStoredOnce()
    {
        var number = NewNumber("INV");

        var responses = await Task.WhenAll(SendAsync(InvoiceBody(number)), SendAsync(InvoiceBody(number)));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        (await InboxAsync(Shipper)).Where(invoice => invoice.DocumentNumber == number).Should().ContainSingle();
    }

    [Fact]
    public async Task ACreditNote_IsShownWithTheInvoiceAndLineItCreditsAndItsReason_AboveTheInvoiceItCorrects()
    {
        var invoiceNumber = NewNumber("INV");
        var creditNoteNumber = NewNumber("CN");
        using var invoice = await SendAsync(InvoiceBody(invoiceNumber));
        await Task.Delay(TimeSpan.FromMilliseconds(20), Ct);

        using var creditNote = await SendAsync(CreditNoteBody(creditNoteNumber, invoiceNumber));

        creditNote.StatusCode.Should().Be(HttpStatusCode.OK);
        var inbox = await InboxAsync(Shipper);
        var shown = inbox.Should().ContainSingle(document => document.DocumentNumber == creditNoteNumber).Subject;
        shown.DocumentType.Should().Be(InvoiceDocumentType.CreditNote);
        shown.Credit.Should().Be(new CreditedLine(invoiceNumber, "TO-0000042", "The pallets were delivered damaged."));
        shown.TotalWithVat.Should().Be(72.60m);
        inbox.Select(document => document.DocumentNumber).ToList().IndexOf(creditNoteNumber)
            .Should().BeLessThan(inbox.Select(document => document.DocumentNumber).ToList().IndexOf(invoiceNumber));
    }

    [Fact]
    public async Task ADocument_IsShownOnlyInTheInboxOfTheShipperItWasSentTo()
    {
        var number = NewNumber("INV");

        using var response = await SendAsync(InvoiceBody(number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await InboxAsync(OtherShipper)).Should().NotContain(invoice => invoice.DocumentNumber == number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-key")]
    public async Task ADocumentWithoutTheShippersCredential_IsRefused_AndStoredNowhere(string? key)
    {
        var number = NewNumber("INV");

        using var response = await SendAsync(InvoiceBody(number), apiKey: key);

        // The portal answers 401, which its status code pages re-execute as the POST of a page and so turn into 400 when the answer has no body: the same for every
        // endpoint of the portal that asks for a key. Either way the caller is refused.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
        (await InboxAsync(Shipper)).Should().NotContain(invoice => invoice.DocumentNumber == number);
    }

    [Fact]
    public async Task ADocumentForAShipperTheyAreNot_IsRefusedAsAnUnknownCaller()
    {
        var number = NewNumber("INV");

        using var response = await SendAsync(InvoiceBody(number), shipperPartyId: "SHP-9999");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
        (await InboxAsync(Shipper)).Should().NotContain(invoice => invoice.DocumentNumber == number);
    }

    [Fact]
    public async Task ADocumentThatBreaksTheFormat_IsRefusedWith400_AndStoredNowhere()
    {
        var number = NewNumber("INV");
        var body = InvoiceBody(number);
        body["documentDate"] = "2026-10-19";
        body["currency"] = "USD";

        using var response = await SendAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await InboxAsync(Shipper)).Should().NotContain(invoice => invoice.DocumentNumber == number);
    }

    [Fact]
    public async Task ADocumentWithoutACorrelationId_IsRefusedWith400()
    {
        var number = NewNumber("INV");

        using var response = await SendAsync(InvoiceBody(number), correlationId: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await InboxAsync(Shipper)).Should().NotContain(invoice => invoice.DocumentNumber == number);
    }

    private async Task<IReadOnlyList<Norvia.PartnerPortal.Application.ShipperInvoices.ShipperInvoiceView>> InboxAsync(string persona) =>
        await fixture.ResetOn.Portal.ListShipperInvoicesAsync(new PersonaId(persona), Ct);

    private async Task<HttpResponseMessage> SendAsync(JsonObject body, string? apiKey = PartnerPortalFixture.CarrierKey, string? correlationId = "", string shipperPartyId = ShipperPartyId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/shipper/v1/{shipperPartyId}/invoices", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        if (apiKey is not null)
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        // An empty default stands for the document's own correlation ID, so a test names one only to leave it out.
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId.Length == 0 ? "corr-" + body["documentNumber"]!.GetValue<string>() : correlationId);
        }

        return await fixture.ResetOn.Http.SendAsync(request, Ct);
    }

    private static string NewNumber(string prefix) => FormattableString.Invariant($"{prefix}-2099-{Random.Shared.Next(0, 1_000_000):D6}");

    private static JsonObject InvoiceBody(string number) => new()
    {
        ["documentType"] = "INVOICE",
        ["documentNumber"] = number,
        ["documentDate"] = "19-10-2026",
        ["lines"] = new JsonArray(Line(150m)),
        ["currency"] = "EUR",
        ["totalWithoutVat"] = 150m,
        ["vat"] = 31.50m,
        ["totalWithVat"] = 181.50m,
    };

    private static JsonObject CreditNoteBody(string number, string invoiceNumber) => new()
    {
        ["documentType"] = "CREDIT_NOTE",
        ["documentNumber"] = number,
        ["documentDate"] = "20-10-2026",
        ["creditedInvoiceNumber"] = invoiceNumber,
        ["creditedTransportOrderNumber"] = "TO-0000042",
        ["reason"] = "The pallets were delivered damaged.",
        ["lines"] = new JsonArray(new JsonObject
        {
            ["transportOrderNumber"] = "TO-0000042",
            ["bookingRef"] = "BD-2026-1187",
            ["deliveryDate"] = "16-10-2026",
            ["chargeLines"] = new JsonArray(new JsonObject { ["code"] = "FREIGHT", ["amount"] = 60m }),
            ["lineTotal"] = 60m,
        }),
        ["currency"] = "EUR",
        ["totalWithoutVat"] = 60m,
        ["vat"] = 12.60m,
        ["totalWithVat"] = 72.60m,
    };

    private static JsonObject Line(decimal total) => new()
    {
        ["transportOrderNumber"] = "TO-0000042",
        ["bookingRef"] = "BD-2026-1187",
        ["deliveryDate"] = "16-10-2026",
        ["chargeLines"] = new JsonArray(
            new JsonObject { ["code"] = "FREIGHT", ["amount"] = 96.40m },
            new JsonObject { ["code"] = "MINIMUM_CHARGE_ADJUSTMENT", ["amount"] = 53.60m }),
        ["lineTotal"] = total,
    };
}
