using System.Text.Json.Nodes;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>
/// <c>get_invoice_line</c> through the stand-in to the ERP API's agent view, with Carrier costs and Carrier invoices each test records itself under numbers of its own: the
/// difference, the tolerance, the source as a label, the earlier line of a Duplicate and the ranked candidates of an Unknown booking, equal to what matching (E2) found when it received
/// the invoice, and no Spot remark or personal data. The view is an allowlist, so a field it doesn't name is not in the answer.
/// </summary>
public sealed class GetInvoiceLineTests(FinanceAgentFixture fixture)
{
    // Every property the agent view may carry (the records in Norvia.Erp.Application.CarrierInvoices.AgentView); the answer's JSON names no other.
    private static readonly HashSet<string> AllowedProperties =
    [
        "carrierId", "carrierName", "invoiceNumber", "invoiceDate", "carrierBookingReference", "result", "status", "invoicedAmount", "currency", "tolerance", "difference",
        "carrierCost", "invoicedBy", "candidates", "resolvesAutomatically",
        "carrierBookingNumber", "total", "source", "deliveredOn", "amount", "rank",
    ];

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task APriceMismatch_IsReadWithTheDifferenceTheToleranceAndTheSourceLabel_AsMatchingFoundIt()
    {
        var (booking, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (reference, 95m));

        var view = await ReadAsync(invoiceNumber, reference, received);

        var found = FinanceAgentCalls.LineOf(received, reference);
        view["result"]!.GetValue<string>().Should().Be("PriceMismatch").And.Be(found["result"]!.GetValue<string>());
        view["status"]!.GetValue<string>().Should().Be("ToDecide").And.Be(found["status"]!.GetValue<string>());
        view["invoicedAmount"]!.GetValue<decimal>().Should().Be(95m);
        view["invoicedAmount"]!.GetValue<decimal>().Should().Be(found["amount"]!.GetValue<decimal>());
        view["difference"]!.GetValue<decimal>().Should().Be(found["amount"]!.GetValue<decimal>() - 80m, "the invoiced amount E2 matched, less the agreed total");
        view["tolerance"]!.GetValue<decimal>().Should().Be(0m);
        view["carrierName"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        view["carrierCost"]!["carrierBookingNumber"]!.GetValue<string>().Should().Be(booking).And.Be(found["carrierBookingNumber"]!.GetValue<string>());
        view["carrierCost"]!["total"]!.GetValue<decimal>().Should().Be(80m);
        view["carrierCost"]!["status"]!.GetValue<string>().Should().Be("Open");
        view["carrierCost"]!["source"]!.GetValue<string>().Should().Be("Spot");
        view["invoicedBy"].Should().BeNull();
        view["candidates"]!.AsArray().Should().BeEmpty();
        view["resolvesAutomatically"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task ACostFromARateCard_IsReadWithTheRateCardVersionAsItsSource()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 120m, _cancellationToken, rateCard: true);
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (reference, 118.5m));

        var view = await ReadAsync(invoiceNumber, reference, received);

        view["carrierCost"]!["source"]!.GetValue<string>().Should().Be("Rate Card version 2");
        view["difference"]!.GetValue<decimal>().Should().Be(-1.5m, "the Carrier claims less than the agreed total");
    }

    [Fact]
    public async Task ADuplicate_IsReadWithTheEarlierInvoiceAndLineThatInvoicedTheCarrierCost()
    {
        var (booking, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken);
        var earlier = FinanceAgentCalls.NewInvoiceNumber();
        var first = await FinanceAgentCalls.ReceiveAsync(fixture, earlier, _cancellationToken, lines: (reference, 100m));
        first["status"]!.GetValue<string>().Should().Be("Approved");
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (reference, 100m));

        var view = await ReadAsync(invoiceNumber, reference, received);

        view["result"]!.GetValue<string>().Should().Be("Duplicate").And.Be(FinanceAgentCalls.LineOf(received, reference)["result"]!.GetValue<string>());
        view["status"]!.GetValue<string>().Should().Be("ToDecide");
        view["difference"]!.GetValue<decimal>().Should().Be(0m);
        view["carrierCost"]!["carrierBookingNumber"]!.GetValue<string>().Should().Be(booking);
        view["carrierCost"]!["status"]!.GetValue<string>().Should().Be("Invoiced");
        var invoicedBy = view["invoicedBy"]!;
        var earlierLine = FinanceAgentCalls.LineOf(first, reference);
        invoicedBy["invoiceNumber"]!.GetValue<string>().Should().Be(first["invoiceNumber"]!.GetValue<string>()).And.Be(earlier);
        invoicedBy["carrierBookingReference"]!.GetValue<string>().Should().Be(earlierLine["carrierBookingReference"]!.GetValue<string>());
        invoicedBy["status"]!.GetValue<string>().Should().Be(earlierLine["status"]!.GetValue<string>());
        invoicedBy["amount"]!.GetValue<decimal>().Should().Be(earlierLine["amount"]!.GetValue<decimal>());
        view["candidates"]!.AsArray().Should().BeEmpty();
        view["resolvesAutomatically"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task AnUnknownBooking_IsReadWithTheLikelyCandidatesOfItsCarrierRankedBestFirst_AndResolvesAutomatically()
    {
        var reference = FinanceAgentCalls.NewReference();
        var (bestBooking, _) = await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken, reference);
        var (nextBooking, _) = await FinanceAgentCalls.RecordCostAsync(fixture, 60m, _cancellationToken, reference[..^2] + "ZZ");
        await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken, reference, FinanceAgentCalls.OtherCarrier);
        var mangled = reference.Replace("-", string.Empty, StringComparison.Ordinal);
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (mangled, 100m));

        var view = await ReadAsync(invoiceNumber, mangled, received);

        var found = FinanceAgentCalls.LineOf(received, mangled);
        view["result"]!.GetValue<string>().Should().Be("UnknownBooking").And.Be(found["result"]!.GetValue<string>());
        view["status"]!.GetValue<string>().Should().Be("ToDecide");
        view["carrierCost"].Should().BeNull();
        view["difference"].Should().BeNull();
        view["invoicedBy"].Should().BeNull();
        view["resolvesAutomatically"]!.GetValue<bool>().Should().BeTrue("the line is matched again when its Carrier cost arrives");
        var candidates = view["candidates"]!.AsArray().Select(candidate => candidate!.AsObject()).ToList();
        candidates.Count.Should().BeInRange(2, 3, "the ERP offers at most three, and the shared database may hold a third likely cost of another test");
        candidates.Select(candidate => candidate["rank"]!.GetValue<int>()).Should().Equal(Enumerable.Range(1, candidates.Count));
        candidates[0]["carrierBookingNumber"]!.GetValue<string>().Should().Be(bestBooking);
        candidates[0]["total"]!.GetValue<decimal>().Should().Be(100m);
        candidates[0]["difference"]!.GetValue<decimal>().Should().Be(0m);
        candidates[0]["source"]!.GetValue<string>().Should().Be("Spot");
        candidates[0]["deliveredOn"]!.GetValue<string>().Should().Be("2026-10-16");
        candidates[1]["carrierBookingNumber"]!.GetValue<string>().Should().Be(nextBooking);
        candidates[1]["difference"]!.GetValue<decimal>().Should().Be(40m);
        candidates.Select(candidate => candidate["carrierBookingReference"]!.GetValue<string>()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task AnUnknownBookingWithNothingLikelyToOffer_IsReadWithNoCandidates()
    {
        var reference = "UNLIKE-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (reference, 4_321.09m));

        var view = await ReadAsync(invoiceNumber, reference, received);

        view["result"]!.GetValue<string>().Should().Be("UnknownBooking");
        view["candidates"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task EveryResult_IsReadWithoutTheSpotRemark_AndWithNoFieldThatIsNotOnTheAllowlist()
    {
        var (_, mismatchReference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var (_, duplicateReference) = await FinanceAgentCalls.RecordCostAsync(fixture, 90m, _cancellationToken);
        var unknownReference = FinanceAgentCalls.NewReference();
        await FinanceAgentCalls.RecordCostAsync(fixture, 70m, _cancellationToken, unknownReference.Replace("-", string.Empty, StringComparison.Ordinal));
        await FinanceAgentCalls.ReceiveAsync(fixture, FinanceAgentCalls.NewInvoiceNumber(), _cancellationToken, lines: (duplicateReference, 90m));
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(
            fixture, invoiceNumber, _cancellationToken, lines: [(mismatchReference, 85m), (duplicateReference, 90m), (unknownReference, 70m)]);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        foreach (var reference in new[] { mismatchReference, duplicateReference, unknownReference })
        {
            var (isError, text) = await client.GetInvoiceLineAsync(FinanceAgentCalls.Carrier, invoiceNumber, reference, received["correlationId"]!.GetValue<string>(), _cancellationToken);

            isError.Should().BeFalse(text);
            ShouldBeOnlyWhatTheAgentMayRead(text);
        }
    }

    [Fact]
    public async Task ALineOfAnotherCarriersInvoice_IsRefused_NotRead()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (reference, 95m));
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, _) = await client.GetInvoiceLineAsync(
            FinanceAgentCalls.OtherCarrier, invoiceNumber, reference, received["correlationId"]!.GetValue<string>(), _cancellationToken);

        isError.Should().BeTrue("the other Carrier has no such invoice");
    }

    private async Task<JsonNode> ReadAsync(string invoiceNumber, string reference, JsonObject received)
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, text) = await client.GetInvoiceLineAsync(
            FinanceAgentCalls.Carrier, invoiceNumber, reference, received["correlationId"]!.GetValue<string>(), _cancellationToken);

        isError.Should().BeFalse(text);
        return ShouldBeOnlyWhatTheAgentMayRead(text);
    }

    // The view names no property off the allowlist and carries nothing of the Planner's remark, a person's name, phone number or email among them.
    private static JsonNode ShouldBeOnlyWhatTheAgentMayRead(string text)
    {
        text.Should().NotContain(FinanceAgentCalls.SpotRemark).And.NotContainAny(FinanceAgentCalls.NeverForTheAgent).And.NotContainEquivalentOf("remark");
        var view = JsonNode.Parse(text)!;
        PropertyNames(view).Should().BeSubsetOf(AllowedProperties);
        return view;
    }

    private static IEnumerable<string> PropertyNames(JsonNode? node) => node switch
    {
        JsonObject properties => properties.SelectMany(property => PropertyNames(property.Value).Prepend(property.Key)),
        JsonArray items => items.SelectMany(PropertyNames),
        _ => [],
    };
}
