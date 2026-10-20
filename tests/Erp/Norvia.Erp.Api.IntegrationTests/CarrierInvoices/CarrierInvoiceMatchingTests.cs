using System.Net;
using Norvia.Erp.Application.CarrierInvoices.List;

namespace Norvia.Erp.Api.IntegrationTests.CarrierInvoices;

/// <summary>
/// Receiving a Carrier invoice and matching its lines through the ERP billing API (E2, user stories 2, 3, 5, 10 and 11): an invoice whose lines all match is Approved at once,
/// the other lines wait for Finance, an Unknown booking line is matched again when its Carrier cost arrives, no Carrier is paid for another's booking or for a booking twice,
/// and an invoice number is the Carrier's own.
/// </summary>
public sealed class CarrierInvoiceMatchingTests(ErpFixture fixture) : CarrierTest(fixture)
{
    private const string InvoiceNumber = "VDT-2026-0100";

    [Fact]
    public async Task AnInvoiceWhoseLinesAllMatch_IsApprovedAtOnce_InvoicesItsCarrierCosts_AndPublishesCarrierInvoiceApprovedOnce()
    {
        await RecordCostAsync("CB-0000301", "BK-A", 100m);
        await RecordCostAsync("CB-0000302", "BK-B", 80m);

        var invoice = await ReceiveAsync(InvoiceNumber, ("BK-A", 100m), ("BK-B", 80m));

        invoice["status"]!.GetValue<string>().Should().Be("Approved");
        invoice["approvedAmount"]!.GetValue<decimal>().Should().Be(180m);
        invoice["lines"]!.AsArray().Select(line => line!["result"]!.GetValue<string>()).Should().OnlyContain(result => result == "Matched");
        invoice["lines"]!.AsArray().Select(line => line!["status"]!.GetValue<string>()).Should().OnlyContain(status => status == "Matched");
        (await OpenCostsAsync()).Should().BeEmpty("both Carrier costs are Invoiced");
        var message = (await PublishedAsync("CarrierInvoiceApproved")).Should().ContainSingle().Subject;
        message.Topic.Should().Be("invoicing-events");
        message.CorrelationId.Should().Be(invoice["correlationId"]!.GetValue<string>());
        message.DocumentNumber.Should().Be(InvoiceNumber);
        message.Body["carrierId"]!.GetValue<string>().Should().Be(Carrier);
        message.Body["approvedAmount"]!.GetValue<decimal>().Should().Be(180m);
        message.Body["lines"]!.AsArray().Select(line => line!["result"]!.GetValue<string>()).Should().OnlyContain(result => result == "Matched");
    }

    [Fact]
    public async Task ALineThatDiffersFromItsCarrierCost_IsAPriceMismatch_AndWaitsForFinance_WithItsCarrierCostBesideIt()
    {
        await RecordCostAsync("CB-0000301", "BK-A", 100m);
        await RecordCostAsync("CB-0000302", "BK-B", 80m);

        var invoice = await ReceiveAsync(InvoiceNumber, ("BK-A", 100m), ("BK-B", 95m));

        invoice["status"]!.GetValue<string>().Should().Be("Received");
        invoice["approvedAmount"].Should().BeNull();
        ResultOf(invoice, "BK-A").Should().Be("Matched");
        ResultOf(invoice, "BK-B").Should().Be("PriceMismatch");
        LineOf(invoice, "BK-B")["status"]!.GetValue<string>().Should().Be("ToDecide");
        LineOf(invoice, "BK-B")["acceptedAmount"].Should().BeNull();
        (await OpenCostsAsync()).Should().Equal("BK-B");
        (await PublishedAsync("CarrierInvoiceApproved")).Should().BeEmpty("a line still waits");
        var toDecide = (await Finance.AskAsync(new ListCarrierInvoicesToDecideQuery(), Ct)).Should().ContainSingle().Subject;
        toDecide.Invoice.InvoiceNumber.Should().Be(InvoiceNumber);
        var cost = toDecide.Costs["CB-0000302"];
        cost.Total.Should().Be(80m);
        cost.Source.Kind.Should().Be("Spot");
        cost.Source.Remark.Should().Be("Spot price agreed by phone.");
    }

    [Fact]
    public async Task ALineThatDiffersByACent_IsAPriceMismatch_BecauseTheToleranceIsZeroByDefault()
    {
        await RecordCostAsync("CB-0000301", "BK-A", 100m);

        var invoice = await ReceiveAsync(InvoiceNumber, ("BK-A", 100.01m));

        ResultOf(invoice, "BK-A").Should().Be("PriceMismatch");
        invoice["status"]!.GetValue<string>().Should().Be("Received");
    }

    [Fact]
    public async Task ALineWithoutACarrierCost_IsAnUnknownBooking_AndWaitsForFinance()
    {
        var invoice = await ReceiveAsync(InvoiceNumber, ("BK-X", 50m));

        invoice["status"]!.GetValue<string>().Should().Be("Received");
        ResultOf(invoice, "BK-X").Should().Be("UnknownBooking");
        LineOf(invoice, "BK-X")["carrierBookingNumber"].Should().BeNull();
        (await PublishedAsync("CarrierInvoiceApproved")).Should().BeEmpty();
    }

    [Fact]
    public async Task ALineForABookingAlreadyInvoiced_IsADuplicate_NeverPaidTwice_AndCanOnlyBeRejected()
    {
        await RecordCostAsync("CB-0000301", "BK-A", 100m);
        await ReceiveAsync(InvoiceNumber, ("BK-A", 100m));

        var second = await ReceiveAsync("VDT-2026-0101", ("BK-A", 100m));

        second["status"]!.GetValue<string>().Should().Be("Received");
        ResultOf(second, "BK-A").Should().Be("Duplicate");
        using var accept = await DecideAsync("VDT-2026-0101", "BK-A", "accept");
        accept.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(accept)).Should().Be("CarrierInvoiceLineHasNoOpenCarrierCost");
        using var reject = await DecideAsync("VDT-2026-0101", "BK-A", "reject", new { reason = "Invoiced before." });
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PublishedAsync("CarrierInvoiceApproved")).Should().HaveCount(2, "the first invoice, and the second once its last line was decided");
    }

    [Fact]
    public async Task AReferenceThatBelongsToAnotherCarrier_IsAnUnknownBooking_AndLeavesTheOwnersCarrierCostOpen()
    {
        await RecordCostAsync("CB-0000301", "BK-A", 100m);

        var invoice = await ReceiveAsync(OtherCarrier, "KSP-2026-0001", ("BK-A", 100m));

        ResultOf(invoice, "BK-A").Should().Be("UnknownBooking");
        invoice["status"]!.GetValue<string>().Should().Be("Received");
        (await OpenCostsAsync()).Should().Equal("BK-A");
        (await PublishedAsync("CarrierInvoiceApproved")).Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnknownBookingLine_IsMatchedWhenItsCarrierCostArrives_AndApprovesTheInvoiceWhenItWasTheLastLine()
    {
        await RecordCostAsync("CB-0000301", "BK-A", 100m);
        await ReceiveAsync(InvoiceNumber, ("BK-A", 100m), ("BK-X", 50m));
        (await PublishedAsync("CarrierInvoiceApproved")).Should().BeEmpty();

        await RecordCostAsync("CB-0000303", "BK-X", 50m);

        var invoice = await ShownAsync(InvoiceNumber);
        invoice.Status.Should().Be("Approved");
        invoice.ApprovedAmount.Should().Be(150m);
        invoice.Lines.Single(line => line.CarrierBookingReference == "BK-X").Should().Match<Application.CarrierInvoices.CarrierInvoiceLineView>(
            line => line.Result == "Matched" && line.Status == "Matched" && line.CarrierBookingNumber == "CB-0000303");
        (await OpenCostsAsync()).Should().BeEmpty("the Carrier cost that arrived is Invoiced as it is recorded");
        var message = (await PublishedAsync("CarrierInvoiceApproved")).Should().ContainSingle().Subject;
        message.DocumentNumber.Should().Be(InvoiceNumber);
        message.Body["approvedAmount"]!.GetValue<decimal>().Should().Be(150m);
    }

    [Fact]
    public async Task AnUnknownBookingLine_WhoseCarrierCostArrivesAtAnotherAmount_BecomesAPriceMismatch_AndKeepsWaiting()
    {
        await ReceiveAsync(InvoiceNumber, ("BK-X", 50m));

        await RecordCostAsync("CB-0000303", "BK-X", 45m);

        var invoice = await ShownAsync(InvoiceNumber);
        invoice.Status.Should().Be("Received");
        invoice.Lines.Single().Should().Match<Application.CarrierInvoices.CarrierInvoiceLineView>(line => line.Result == "PriceMismatch" && line.Status == "ToDecide");
        (await OpenCostsAsync()).Should().Equal("BK-X");
        (await PublishedAsync("CarrierInvoiceApproved")).Should().BeEmpty();
    }

    [Fact]
    public async Task ACarrierCostOfAnotherCarrier_DoesNotMatchAnUnknownBookingLine()
    {
        await ReceiveAsync(InvoiceNumber, ("BK-X", 50m));

        await RecordCostAsync("CB-0000303", "BK-X", 50m, OtherCarrier);

        (await ShownAsync(InvoiceNumber)).Lines.Single().Result.Should().Be("UnknownBooking");
        (await PublishedAsync("CarrierInvoiceApproved")).Should().BeEmpty();
    }

    [Fact]
    public async Task ARepeatedSubmission_AnswersTheOriginalResultWith200_AndPublishesNothingMore()
    {
        await RecordCostAsync("CB-0000301", "BK-A", 100m);
        var invoice = CarrierInvoice(Carrier, InvoiceNumber, ("BK-A", 100m));
        using var first = await PostInvoiceAsync(invoice);

        using var repeat = await PostInvoiceAsync(invoice);

        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(Ct));
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        var answer = await BodyOfAsync(repeat);
        answer["repeat"]!.GetValue<bool>().Should().BeTrue();
        answer["invoice"]!["status"]!.GetValue<string>().Should().Be("Approved", "the original result, not a second match against an Invoiced Carrier cost");
        ResultOf(answer["invoice"]!.AsObject(), "BK-A").Should().Be("Matched");
        (await PublishedAsync("CarrierInvoiceApproved")).Should().ContainSingle();
    }

    [Fact]
    public async Task AnInvoiceNumberUsedForDifferentContent_IsRefusedWith409_AndChangesNothing()
    {
        await RecordCostAsync("CB-0000301", "BK-A", 100m);
        await RecordCostAsync("CB-0000302", "BK-B", 80m);
        await ReceiveAsync(InvoiceNumber, ("BK-A", 100m));

        using var response = await PostInvoiceAsync(CarrierInvoice(Carrier, InvoiceNumber, ("BK-B", 80m)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOfAsync(response)).Should().Be("InvoiceNumberUsed");
        (await OpenCostsAsync()).Should().Equal("BK-B");
        (await PublishedAsync("CarrierInvoiceApproved")).Should().ContainSingle();
    }

    [Fact]
    public async Task TwoCarriersMayUseTheSameInvoiceNumber()
    {
        await RecordCostAsync("CB-0000301", "BK-A", 100m);
        await RecordCostAsync("CB-0000302", "BK-B", 80m, OtherCarrier);

        var first = await ReceiveAsync(Carrier, InvoiceNumber, ("BK-A", 100m));
        var second = await ReceiveAsync(OtherCarrier, InvoiceNumber, ("BK-B", 80m));

        first["status"]!.GetValue<string>().Should().Be("Approved");
        second["status"]!.GetValue<string>().Should().Be("Approved");
        (await PublishedAsync("CarrierInvoiceApproved")).Should().HaveCount(2);
    }

    [Fact]
    public async Task AnInvoiceOfACarrierTheErpDoesNotHave_IsRefusedWith422()
    {
        using var response = await PostInvoiceAsync(CarrierInvoice("CAR-9999", InvoiceNumber, ("BK-A", 100m)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(response)).Should().Be("CarrierNotFound");
    }

    [Fact]
    public async Task AnInvoiceWithoutLines_IsRefusedWith400()
    {
        using var response = await PostInvoiceAsync(CarrierInvoice(Carrier, InvoiceNumber));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).Should().Be("CarrierInvoiceWithoutLines");
    }

    [Fact]
    public async Task ALineWithoutAReference_IsRefusedWith400()
    {
        using var response = await PostInvoiceAsync(CarrierInvoice(Carrier, InvoiceNumber, ("  ", 100m)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).Should().Be("CarrierInvoiceLineWithoutReference");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ALineWithAnAmountOfZeroOrLess_IsRefusedWith400(decimal amount)
    {
        using var response = await PostInvoiceAsync(CarrierInvoice(Carrier, InvoiceNumber, ("BK-A", amount)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).Should().Be("CarrierInvoiceLineAmountNotAboveZero");
    }

    [Fact]
    public async Task ASecondLineWithTheSameReference_IsRefusedWith400_AndNothingIsRecorded()
    {
        await RecordCostAsync("CB-0000301", "BK-A", 100m);

        using var response = await PostInvoiceAsync(CarrierInvoice(Carrier, InvoiceNumber, ("BK-A", 60m), ("BK-A", 40m)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOfAsync(response)).Should().Be("CarrierInvoiceReferenceRepeated");
        (await OpenCostsAsync()).Should().Equal("BK-A");
        (await Finance.AskAsync(new ListCarrierInvoicesQuery(), Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task ACallerWithoutTheReceiveRole_IsRefused_AndReceivesNothing()
    {
        foreach (var roles in new string[][]
        {
            [],
            [ErpFixture.RecordCarrierCostRole],
            [ErpFixture.ReportCarrierInvoiceRole],
            [ErpFixture.ManageCarrierInvoiceRole],
        })
        {
            using var response = await PostInvoiceAsync(CarrierInvoice(Carrier, InvoiceNumber, ("BK-A", 100m)), roles.Length > 0 ? roles : ["Finance"]);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, because: $"{string.Join(',', roles)} isn't {ErpFixture.ReceiveCarrierInvoiceRole}");
        }

        (await Finance.AskAsync(new ListCarrierInvoicesQuery(), Ct)).Should().BeEmpty();
    }

    private async Task<HttpResponseMessage> DecideAsync(string invoiceNumber, string reference, string decision, object? body = null)
    {
        using var client = Fixture.ApiAs(ErpFixture.ManageCarrierInvoiceRole);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-invoices/{Carrier}/{invoiceNumber}/lines/{reference}/{decision}", UriKind.Relative));
        if (body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
        }

        return await client.SendAsync(request, Ct);
    }
}
