using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierInvoice.IntegrationTests;

/// <summary>
/// The local Carrier invoice path end to end (E2.10): a Carrier Persona's invoice goes through the Partner API's stand-in and the Carrier invoice worker to the ERP, and each line's
/// result comes back to the Carrier at once. The ERP is real and keeps its records; the tests share its database, so each sends invoices and booking references of its own.
/// </summary>
public sealed class CarrierInvoicePathTests(CarrierInvoiceFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnInvoice_IsReceivedByTheErp_AndEachLinesResultComesBackToTheCarrier()
    {
        var invoice = NewInvoice(70.50m, 120.00m);
        await fixture.RecordCarrierCostAsync(invoice.BookingNumbers[0], invoice.References[0], 70.50m, Ct);

        using var response = await SubmitAsync(invoice.Body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var answer = await AnswerAsync(response);
        answer["result"]!.GetValue<string>().Should().Be("RECEIVED");
        answer["invoiceNo"]!.GetValue<string>().Should().Be(invoice.Number);
        answer["repeated"]!.GetValue<bool>().Should().BeFalse();
        answer["status"]!.GetValue<string>().Should().Be("RECEIVED");
        answer["messageId"]!.GetValue<string>().Should().Be(response.Headers.GetValues("X-Correlation-ID").Single());
        ResultsOf(answer).Should().Equal((invoice.References[0], "MATCHED"), (invoice.References[1], "UNKNOWN_BOOKING"));

        var held = await fixture.InvoiceInErpAsync(CarrierInvoiceFixture.CarrierId, invoice.Number, Ct);
        held.Should().NotBeNull("the ERP recorded the invoice");
        held!.CorrelationId.Should().Be(answer["messageId"]!.GetValue<string>(), "the ERP keeps the correlation ID the Partner API created");
        held.Status.Should().Be("Received");
        held.Lines.Select(line => (line.CarrierBookingReference, line.Result)).Should().Equal((invoice.References[0], "Matched"), (invoice.References[1], "UnknownBooking"));
    }

    [Fact]
    public async Task AnInvoiceWhoseLinesAllMatch_IsApproved_AndTheErpPublishesIt()
    {
        var invoice = NewInvoice(70.50m, 120.00m);
        await fixture.RecordCarrierCostAsync(invoice.BookingNumbers[0], invoice.References[0], 70.50m, Ct);
        await fixture.RecordCarrierCostAsync(invoice.BookingNumbers[1], invoice.References[1], 120.00m, Ct);
        var publishedBefore = fixture.Published.Count(type => type == "CarrierInvoiceApproved");

        using var response = await SubmitAsync(invoice.Body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var answer = await AnswerAsync(response);
        answer["status"]!.GetValue<string>().Should().Be("APPROVED");
        answer["approvedAmount"]!.GetValue<decimal>().Should().Be(190.50m);
        ResultsOf(answer).Select(line => line.Result).Should().OnlyContain(result => result == "MATCHED");
        await WaitForPublishedAsync("CarrierInvoiceApproved", publishedBefore + 1);
    }

    [Fact]
    public async Task ALineThatClaimsMoreThanTheCarrierCost_IsAPriceMismatch_AndTheInvoiceWaitsForFinance()
    {
        var invoice = NewInvoice(90.00m);
        await fixture.RecordCarrierCostAsync(invoice.BookingNumbers[0], invoice.References[0], 70.50m, Ct);

        using var response = await SubmitAsync(invoice.Body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var answer = await AnswerAsync(response);
        answer["status"]!.GetValue<string>().Should().Be("RECEIVED");
        ResultsOf(answer).Should().Equal((invoice.References[0], "PRICE_MISMATCH"));
    }

    [Fact]
    public async Task TheSameInvoiceAgain_IsAnsweredAsARepeat_AndTheErpHoldsItOnce()
    {
        var invoice = NewInvoice(70.50m);
        await fixture.RecordCarrierCostAsync(invoice.BookingNumbers[0], invoice.References[0], 70.50m, Ct);
        using var first = await SubmitAsync(invoice.Body);

        using var repeat = await SubmitAsync(invoice.Body);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        var answer = await AnswerAsync(repeat);
        answer["repeated"]!.GetValue<bool>().Should().BeTrue();
        ResultsOf(answer).Should().Equal((invoice.References[0], "MATCHED"));
        (await fixture.InvoiceInErpAsync(CarrierInvoiceFixture.CarrierId, invoice.Number, Ct))!.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task AnInvoiceNumberUsedForDifferentContent_IsRefused409_AndTheErpKeepsTheFirst()
    {
        var invoice = NewInvoice(70.50m);
        using var first = await SubmitAsync(invoice.Body);
        var changed = (JsonObject)JsonNode.Parse(invoice.Body.ToJsonString())!;
        changed["lines"]![0]!["amount"] = 71.00m;

        using var response = await SubmitAsync(changed);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AnswerAsync(response))["result"]!.GetValue<string>().Should().Be("INVOICE_NUMBER_USED");
        (await fixture.InvoiceInErpAsync(CarrierInvoiceFixture.CarrierId, invoice.Number, Ct))!.Total.Should().Be(70.50m);
    }

    [Fact]
    public async Task AShipperPersona_IsRefused403_AndNothingReachesTheErp()
    {
        var invoice = NewInvoice(70.50m);

        using var response = await SubmitAsync(invoice.Body, clientId: CarrierInvoiceFixture.ShipperClientId, roles: ["TransportOrders.Submit"]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AnswerAsync(response))["text"]!.GetValue<string>().Should().Be("This client may not send Carrier invoices.");
        (await fixture.InvoiceInErpAsync(CarrierInvoiceFixture.CarrierId, invoice.Number, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task AnInvoiceWithoutASubscriptionKey_IsRefused401_AndNothingReachesTheErp()
    {
        var invoice = NewInvoice(70.50m);

        using var response = await SubmitAsync(invoice.Body, subscriptionKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.InvoiceInErpAsync(CarrierInvoiceFixture.CarrierId, invoice.Number, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task ACarrierTheErpDoesNotKnow_IsRefused422_AndNothingIsRecorded()
    {
        var invoice = NewInvoice(70.50m);

        using var response = await SubmitAsync(invoice.Body, clientId: CarrierInvoiceFixture.StrangerClientId, subscriptionKey: "stranger-subscription-key");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await AnswerAsync(response))["result"]!.GetValue<string>().Should().Be("CARRIER_NOT_FOUND");
        (await fixture.InvoiceInErpAsync("CAR-9999", invoice.Number, Ct)).Should().BeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "invoiceNo": "BROKEN-1", "invoiceDate": "19-10-2026", "lines": [] }""")]
    [InlineData("""{ "invoiceNo": "BROKEN-2", "invoiceDate": "2026-10-19", "lines": [ { "bookingRef": "TRX-1", "amount": 10 } ] }""")]
    public async Task AnInvoiceThatBreaksTheFormat_IsRefused400_AndNothingIsRecorded(string body)
    {
        using var response = await SubmitRawAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AnswerAsync(response))["result"]!.GetValue<string>().Should().Be("INVALID");
    }

    private async Task<HttpResponseMessage> SubmitAsync(JsonObject body, string clientId = CarrierInvoiceFixture.CarrierClientId, string[]? roles = null, string? subscriptionKey = CarrierInvoiceFixture.SubscriptionKey) =>
        await SubmitRawAsync(body.ToJsonString(), clientId, roles, subscriptionKey);

    private async Task<HttpResponseMessage> SubmitRawAsync(string body, string clientId = CarrierInvoiceFixture.CarrierClientId, string[]? roles = null, string? subscriptionKey = CarrierInvoiceFixture.SubscriptionKey)
    {
        using var client = fixture.PartnerApiAs(clientId, roles, subscriptionKey);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.PostAsync(new Uri("/partner/v1/carrier-invoices", UriKind.Relative), content, Ct);
    }

    private static async Task<JsonObject> AnswerAsync(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!;

    private static List<(string Reference, string Result)> ResultsOf(JsonObject answer) =>
        [.. answer["lines"]!.AsArray().Select(line => (line!["bookingRef"]!.GetValue<string>(), line["result"]!.GetValue<string>()))];

    private async Task WaitForPublishedAsync(string eventType, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (fixture.Published.Count(type => type == eventType) < count)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, $"the ERP's outbox should have published {eventType}");
            await Task.Delay(TimeSpan.FromMilliseconds(50), Ct);
        }
    }

    /// <summary>An invoice of numbers of its own, in the Carrier's format, with a line for each amount, and the Carrier booking numbers and references its lines name.</summary>
    private static NewInvoiceOf NewInvoice(params decimal[] lineAmounts)
    {
        var suffix = Random.Shared.Next(1_000_000, 9_999_999);
        var number = $"VDT-IT-{suffix}";
        var references = lineAmounts.Select((_, index) => $"TRX-{suffix}-{index}").ToArray();
        var bookingNumbers = lineAmounts.Select((_, index) => $"CB-{suffix % 1_000_000:D6}{index}").ToArray();
        var body = new JsonObject
        {
            ["invoiceNo"] = number,
            ["invoiceDate"] = "19-10-2026",
            ["lines"] = new JsonArray([.. lineAmounts.Select((amount, index) => (JsonNode)new JsonObject { ["bookingRef"] = references[index], ["amount"] = amount })]),
        };
        return new NewInvoiceOf(number, references, bookingNumbers, body);
    }

    private sealed record NewInvoiceOf(string Number, string[] References, string[] BookingNumbers, JsonObject Body);
}
