using System.Text.Json.Nodes;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>
/// <c>get_invoice_summary</c> through the stand-in to the ERP API's agent view, for Carrier invoices each test receives under numbers of its own: the lines grouped by result with the
/// counts the ERP works out, which of them may still resolve, and no Spot remark or personal data. The view is an allowlist, so a field it doesn't name is not in the answer.
/// </summary>
public sealed class GetInvoiceSummaryTests(FinanceAgentFixture fixture)
{
    // Every property the summary may carry (the records in Norvia.Erp.Application.CarrierInvoices.AgentView); the answer's JSON names no other.
    private static readonly HashSet<string> AllowedProperties =
    [
        "carrierId", "carrierName", "invoiceNumber", "invoiceDate", "receivedAt", "status", "total", "currency", "approvedAmountSoFar", "lines", "toDecide", "resolveAutomatically",
        "byResult", "result", "amount",
    ];

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnInvoiceOfTwelveLines_IsReadWithTheLinesGroupedByResult_AndTheCountsOfTheErp()
    {
        var (invoiceNumber, received) = await ReceiveMixedInvoiceAsync();

        var summary = await ReadAsync(invoiceNumber, received);

        summary["carrierId"]!.GetValue<string>().Should().Be(FinanceAgentCalls.Carrier);
        summary["carrierName"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        summary["invoiceNumber"]!.GetValue<string>().Should().Be(invoiceNumber);
        summary["invoiceDate"]!.GetValue<string>().Should().Be("2026-10-19");
        summary["status"]!.GetValue<string>().Should().Be("Received").And.Be(received["status"]!.GetValue<string>());
        summary["currency"]!.GetValue<string>().Should().Be("EUR");
        summary["total"]!.GetValue<decimal>().Should().Be(10m + 20m + 30m + 40m + 50m + 95m + 125m + 210m + 33m + 44m + 60m + 70m);
        summary["approvedAmountSoFar"]!.GetValue<decimal>().Should().Be(150m, "the five Matched lines are the only ones decided so far");
        summary["lines"]!.GetValue<int>().Should().Be(12);
        summary["toDecide"]!.GetValue<int>().Should().Be(7);
        summary["resolveAutomatically"]!.GetValue<int>().Should().Be(2, "only the Unknown booking lines are matched again when their Carrier cost arrives");
        var groups = summary["byResult"]!.AsArray().Select(group => group!.AsObject()).ToList();
        groups.Select(group => group["result"]!.GetValue<string>()).Should().Equal("Matched", "PriceMismatch", "UnknownBooking", "Duplicate");
        ShouldBeGroup(groups[0], lines: 5, toDecide: 0, amount: 150m, resolveAutomatically: 0);
        ShouldBeGroup(groups[1], lines: 3, toDecide: 3, amount: 430m, resolveAutomatically: 0);
        ShouldBeGroup(groups[2], lines: 2, toDecide: 2, amount: 77m, resolveAutomatically: 2);
        ShouldBeGroup(groups[3], lines: 2, toDecide: 2, amount: 130m, resolveAutomatically: 0);
    }

    [Fact]
    public async Task AnInvoiceOfMatchedLinesOnly_IsReadApproved_WithNothingToDecideAndEverythingApproved()
    {
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var (_, first) = await FinanceAgentCalls.RecordCostAsync(fixture, 25m, _cancellationToken);
        var (_, second) = await FinanceAgentCalls.RecordCostAsync(fixture, 75m, _cancellationToken);
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: [(first, 25m), (second, 75m)]);

        var summary = await ReadAsync(invoiceNumber, received);

        summary["status"]!.GetValue<string>().Should().Be("Approved");
        summary["total"]!.GetValue<decimal>().Should().Be(100m);
        summary["approvedAmountSoFar"]!.GetValue<decimal>().Should().Be(100m);
        summary["toDecide"]!.GetValue<int>().Should().Be(0);
        summary["resolveAutomatically"]!.GetValue<int>().Should().Be(0);
        var group = summary["byResult"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        ShouldBeGroup(group, lines: 2, toDecide: 0, amount: 100m, resolveAutomatically: 0);
    }

    [Fact]
    public async Task AnInvoiceWithEveryResult_IsReadWithoutTheSpotRemark_AndWithNoFieldThatIsNotOnTheAllowlist()
    {
        var (invoiceNumber, received) = await ReceiveMixedInvoiceAsync();
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, text) = await client.GetInvoiceSummaryAsync(FinanceAgentCalls.Carrier, invoiceNumber, received["correlationId"]!.GetValue<string>(), _cancellationToken);

        isError.Should().BeFalse(text);
        text.Should().NotContain(FinanceAgentCalls.SpotRemark).And.NotContainAny(FinanceAgentCalls.NeverForTheAgent).And.NotContainEquivalentOf("remark")
            .And.NotContainEquivalentOf("carrierBookingReference", "no line's reference is in the summary");
        PropertyNames(JsonNode.Parse(text)).Should().BeSubsetOf(AllowedProperties);
    }

    [Fact]
    public async Task AnInvoiceOfAnotherCarrier_IsRefused_NotRead()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (reference, 80m));
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, _) = await client.GetInvoiceSummaryAsync(FinanceAgentCalls.OtherCarrier, invoiceNumber, received["correlationId"]!.GetValue<string>(), _cancellationToken);

        isError.Should().BeTrue("the other Carrier has no such invoice");
    }

    // 12 lines: 5 Matched (10, 20, 30, 40, 50), 3 Price mismatch (95, 125, 210), 2 Unknown booking (33, 44) and 2 Duplicate (60, 70).
    private async Task<(string InvoiceNumber, JsonObject Received)> ReceiveMixedInvoiceAsync()
    {
        var lines = new List<(string Reference, decimal Amount)>();
        foreach (var amount in new[] { 10m, 20m, 30m, 40m, 50m })
        {
            var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, amount, _cancellationToken);
            lines.Add((reference, amount));
        }

        foreach (var (agreed, claimed) in new[] { (80m, 95m), (100m, 125m), (150m, 210m) })
        {
            var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, agreed, _cancellationToken);
            lines.Add((reference, claimed));
        }

        foreach (var amount in new[] { 33m, 44m })
        {
            lines.Add(("UNLIKE-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), amount));
        }

        foreach (var amount in new[] { 60m, 70m })
        {
            var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, amount, _cancellationToken);
            var earlier = await FinanceAgentCalls.ReceiveAsync(fixture, FinanceAgentCalls.NewInvoiceNumber(), _cancellationToken, lines: (reference, amount));
            earlier["status"]!.GetValue<string>().Should().Be("Approved", "the Carrier cost is Invoiced by an earlier invoice");
            lines.Add((reference, amount));
        }

        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: [.. lines]);
        return (invoiceNumber, received);
    }

    private async Task<JsonNode> ReadAsync(string invoiceNumber, JsonObject received)
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, text) = await client.GetInvoiceSummaryAsync(FinanceAgentCalls.Carrier, invoiceNumber, received["correlationId"]!.GetValue<string>(), _cancellationToken);

        isError.Should().BeFalse(text);
        return JsonNode.Parse(text)!;
    }

    private static void ShouldBeGroup(JsonObject group, int lines, int toDecide, decimal amount, int resolveAutomatically)
    {
        group["lines"]!.GetValue<int>().Should().Be(lines);
        group["toDecide"]!.GetValue<int>().Should().Be(toDecide);
        group["amount"]!.GetValue<decimal>().Should().Be(amount);
        group["resolveAutomatically"]!.GetValue<int>().Should().Be(resolveAutomatically);
    }

    private static IEnumerable<string> PropertyNames(JsonNode? node) => node switch
    {
        JsonObject properties => properties.SelectMany(property => PropertyNames(property.Value).Prepend(property.Key)),
        JsonArray items => items.SelectMany(PropertyNames),
        _ => [],
    };
}
