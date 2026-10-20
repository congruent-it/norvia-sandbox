using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Erp.Application.CarrierCosts.ListOpen;

namespace Norvia.Erp.Api.IntegrationTests.CarrierInvoices;

/// <summary>
/// The Simulator's Finance decisions on Carrier invoice lines through the ERP billing API (E2.11): accept and reject, the last decision publishing
/// <c>CarrierInvoiceApproved</c> once, the stable codes of a refusal, and who may decide.
/// </summary>
public sealed class CarrierInvoiceDecisionTests(ErpFixture fixture) : ErpTest(fixture)
{
    private const string Carrier = "CAR-0001";
    private const string InvoiceNumber = "VDT-2026-0001";

    // BK-A matches its Carrier cost, BK-B is 95.00 against 80.00 (Price mismatch), BK-X has no Carrier cost (Unknown booking).
    private const string Matched = "BK-A";
    private const string Mismatch = "BK-B";
    private const string Unknown = "BK-X";

    private static string Path(string reference, string decision, string carrier = Carrier, string invoice = InvoiceNumber) =>
        $"/v1/carrier-invoices/{carrier}/{invoice}/lines/{reference}/{decision}";

    private async Task RecordCostAsync(string bookingNumber, string reference, decimal total)
    {
        var number = int.Parse(bookingNumber[3..], System.Globalization.CultureInfo.InvariantCulture);
        var cost = new JsonObject
        {
            ["eventId"] = Guid.NewGuid().ToString(),
            ["eventType"] = "CarrierCostIncurred",
            ["eventVersion"] = 1,
            ["occurredAt"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["carrierBookingNumber"] = bookingNumber,
            ["carrierBookingReference"] = reference,
            ["shipmentNumber"] = $"SH-{number:D7}",
            ["transportOrderNumber"] = $"TO-{number:D7}",
            ["carrierId"] = Carrier,
            ["carrierCost"] = new JsonObject
            {
                ["currency"] = "EUR",
                ["total"] = total,
                ["chargeLines"] = new JsonArray(new JsonObject { ["type"] = "Freight", ["amount"] = total }),
                ["source"] = new JsonObject { ["kind"] = "Spot", ["remark"] = "Spot price agreed by phone." },
                ["calculatedAt"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            },
            ["deliveryTime"] = Delivered.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

        using var client = Fixture.ApiAs(ErpFixture.RecordCarrierCostRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/carrier-costs", UriKind.Relative), cost, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task ReceiveAsync(params (string Reference, decimal Amount)[] lines)
    {
        var invoice = new JsonObject
        {
            ["carrierId"] = Carrier,
            ["invoiceNumber"] = InvoiceNumber,
            ["invoiceDate"] = "2026-10-19",
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["lines"] = new JsonArray([.. lines.Select(line => (JsonNode)new JsonObject { ["carrierBookingReference"] = line.Reference, ["amount"] = line.Amount })]),
        };

        using var client = Fixture.ApiAs(ErpFixture.ReceiveCarrierInvoiceRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/carrier-invoices", UriKind.Relative), invoice, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>An invoice with one Matched line and two that wait for Finance: a Price mismatch and an Unknown booking.</summary>
    private async Task ReceiveInvoiceToDecideAsync()
    {
        await RecordCostAsync("CB-0000101", Matched, 100m);
        await RecordCostAsync("CB-0000102", Mismatch, 80m);
        await ReceiveAsync((Matched, 100m), (Mismatch, 95m), (Unknown, 50m));
    }

    private async Task<HttpResponseMessage> DecideAsync(string reference, string decision, object? body = null, string[]? roles = null, string carrier = Carrier, string invoice = InvoiceNumber)
    {
        using var client = Fixture.ApiAs(roles ?? [ErpFixture.ManageCarrierInvoiceRole]);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Path(reference, decision, carrier, invoice), UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, Ct);
    }

    private static JsonObject Line(JsonObject invoice, string reference) =>
        invoice["lines"]!.AsArray().Select(line => line!.AsObject()).Single(line => line["carrierBookingReference"]!.GetValue<string>() == reference);

    private async Task<IReadOnlyList<string>> OpenCostsAsync() =>
        [.. (await Finance.AskAsync(new ListOpenCarrierCostsQuery(), Ct)).SelectMany(carrier => carrier.Costs).Select(cost => cost.CarrierBookingReference)];

    [Fact]
    public async Task Accept_AtTheInvoicedAmount_AcceptsTheLine_AndInvoicesItsCarrierCost()
    {
        await ReceiveInvoiceToDecideAsync();

        using var response = await DecideAsync(Mismatch, "accept");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var invoice = await BodyOfAsync(response);
        var line = Line(invoice, Mismatch);
        line["status"]!.GetValue<string>().Should().Be("Accepted");
        line["acceptedAmount"]!.GetValue<decimal>().Should().Be(95m);
        line["remark"].Should().BeNull();
        invoice["status"]!.GetValue<string>().Should().Be("Received", "the Unknown booking still waits");
        (await OpenCostsAsync()).Should().BeEmpty("both Carrier costs are Invoiced now");
    }

    [Fact]
    public async Task Accept_AtAnAdjustedAmountWithARemark_AcceptsThatAmount()
    {
        await ReceiveInvoiceToDecideAsync();

        using var response = await DecideAsync(Mismatch, "accept", new { adjustedAmount = 85m, remark = "Waiting time was not agreed." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var line = Line(await BodyOfAsync(response), Mismatch);
        line["status"]!.GetValue<string>().Should().Be("Accepted");
        line["acceptedAmount"]!.GetValue<decimal>().Should().Be(85m);
        line["remark"]!.GetValue<string>().Should().Be("Waiting time was not agreed.");
    }

    [Fact]
    public async Task Reject_WithAReason_RejectsTheLine_AndLeavesItsCarrierCostOpen()
    {
        await ReceiveInvoiceToDecideAsync();

        using var response = await DecideAsync(Mismatch, "reject", new { reason = "Not what we agreed." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var line = Line(await BodyOfAsync(response), Mismatch);
        line["status"]!.GetValue<string>().Should().Be("Rejected");
        line["acceptedAmount"].Should().BeNull();
        line["remark"]!.GetValue<string>().Should().Be("Not what we agreed.");
        (await OpenCostsAsync()).Should().Equal(Mismatch);
    }

    [Fact]
    public async Task TheLastDecision_ApprovesTheInvoice_AndPublishesCarrierInvoiceApprovedOnce()
    {
        await ReceiveInvoiceToDecideAsync();

        using var first = await DecideAsync(Mismatch, "accept", new { adjustedAmount = 85m, remark = "Waiting time was not agreed." });
        (await PublishedAsync("CarrierInvoiceApproved")).Should().BeEmpty("a line still waits");
        using var last = await DecideAsync(Unknown, "reject", new { reason = "We never booked this." });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        last.StatusCode.Should().Be(HttpStatusCode.OK);
        var invoice = await BodyOfAsync(last);
        invoice["status"]!.GetValue<string>().Should().Be("Approved");
        invoice["approvedAmount"]!.GetValue<decimal>().Should().Be(185m, "100.00 matched and 85.00 adjusted; the rejected line adds nothing");
        var message = (await PublishedAsync("CarrierInvoiceApproved")).Should().ContainSingle().Subject;
        message.Topic.Should().Be("invoicing-events");
        message.CorrelationId.Should().Be(invoice["correlationId"]!.GetValue<string>());
        message.DocumentNumber.Should().Be(InvoiceNumber);
        message.Body["approvedAmount"]!.GetValue<decimal>().Should().Be(185m);
        message.Body["lines"]!.AsArray().Select(line => line!["result"]!.GetValue<string>()).Should().BeEquivalentTo("Matched", "Accepted", "Rejected");
    }

    [Fact]
    public async Task ADecisionOnALineAlreadyDecided_IsRefusedWith409_AndPublishesNothingMore()
    {
        await ReceiveInvoiceToDecideAsync();
        await DecideAsync(Mismatch, "accept");
        await DecideAsync(Unknown, "reject", new { reason = "We never booked this." });

        using var again = await DecideAsync(Unknown, "reject", new { reason = "Still not ours." });
        using var acceptMatched = await DecideAsync(Matched, "accept");

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOfAsync(again)).Should().Be("CarrierInvoiceLineAlreadyDecided");
        acceptMatched.StatusCode.Should().Be(HttpStatusCode.Conflict, "a Matched line is decided already");
        (await CodeOfAsync(acceptMatched)).Should().Be("CarrierInvoiceLineAlreadyDecided");
        (await PublishedAsync("CarrierInvoiceApproved")).Should().ContainSingle();
    }

    [Fact]
    public async Task Accept_AUnknownBookingLine_IsRefusedWith422_AndItCanStillBeRejected()
    {
        await ReceiveInvoiceToDecideAsync();

        using var accept = await DecideAsync(Unknown, "accept");
        using var reject = await DecideAsync(Unknown, "reject", new { reason = "We never booked this." });

        accept.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(accept)).Should().Be("CarrierInvoiceLineHasNoOpenCarrierCost");
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("CAR-0001", "VDT-2026-9999", Mismatch, "CarrierInvoiceNotFound")]
    [InlineData("CAR-0002", InvoiceNumber, Mismatch, "CarrierInvoiceNotFound")]
    [InlineData("CAR-0001", InvoiceNumber, "BK-NOPE", "CarrierInvoiceLineNotFound")]
    public async Task ADecisionOnAnInvoiceOrLineTheErpDoesNotHave_IsRefusedWith404(string carrier, string invoice, string reference, string code)
    {
        await ReceiveInvoiceToDecideAsync();

        using var accept = await DecideAsync(reference, "accept", carrier: carrier, invoice: invoice);
        using var reject = await DecideAsync(reference, "reject", new { reason = "Not ours." }, carrier: carrier, invoice: invoice);

        accept.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOfAsync(accept)).Should().Be(code);
        reject.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOfAsync(reject)).Should().Be(code);
    }

    [Theory]
    [InlineData(0, "Waiting time.", "CarrierInvoiceAdjustedAmountNotAboveZero")]
    [InlineData(85, null, "CarrierInvoiceAdjustedAmountNeedsRemark")]
    [InlineData(85, "  ", "CarrierInvoiceAdjustedAmountNeedsRemark")]
    public async Task Accept_WithAnInvalidAdjustment_IsRefusedWith400_AndChangesNothing(decimal adjustedAmount, string? remark, string code)
    {
        await ReceiveInvoiceToDecideAsync();

        using var response = await DecideAsync(Mismatch, "accept", new { adjustedAmount, remark });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).Should().Be(code);
        (await OpenCostsAsync()).Should().BeEquivalentTo(Mismatch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_WithoutAReason_IsRefusedWith400_AndChangesNothing(string? reason)
    {
        await ReceiveInvoiceToDecideAsync();

        using var response = await DecideAsync(Unknown, "reject", new { reason });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).Should().Be("CarrierInvoiceRejectionNeedsReason");
        (await PublishedAsync("CarrierInvoiceApproved")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    public async Task ACallerWithoutTheManageRole_IsRefused_AndDecidesNothing(string decision)
    {
        await ReceiveInvoiceToDecideAsync();

        foreach (var roles in new[]
        {
            Array.Empty<string>(),
            [ErpFixture.ReceiveCarrierInvoiceRole],
            [ErpFixture.ReportRole],
            [ErpFixture.RecordCarrierCostRole],
        })
        {
            using var response = await DecideAsync(Mismatch, decision, new { reason = "Not ours." }, roles);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, because: $"{string.Join(',', roles)} isn't {ErpFixture.ManageCarrierInvoiceRole}");
        }

        (await OpenCostsAsync()).Should().BeEquivalentTo(Mismatch);
    }
}
