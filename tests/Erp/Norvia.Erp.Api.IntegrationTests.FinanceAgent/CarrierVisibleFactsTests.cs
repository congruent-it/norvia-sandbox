using System.Text.Json.Nodes;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>
/// <c>get_carrier_visible_line_facts</c> through the stand-in to the ERP API's rejection reason view (#473): the reason leaves Norvia in the invoice outcome, so the answer names only what the
/// Carrier can see. A line whose cost is a Spot price with a Planner remark shows neither the remark nor the Rate Card version, and the likely correct reference is one of that Carrier's own
/// bookings or none. Each test records the Carrier costs and Carrier invoices it needs under numbers of its own.
/// </summary>
public sealed class CarrierVisibleFactsTests(FinanceAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALineWhoseCostIsASpotPriceWithAPlannerRemark_ShowsTheAgreedAmount_ButNeitherTheRemarkNorTheSource()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var (invoiceNumber, received) = await ReceiveAsync(reference, 95m);

        var text = await RejectionReasonCalls.ReadFactsTextAsync(fixture, FinanceAgentCalls.Carrier, invoiceNumber, reference, CorrelationOf(received), _cancellationToken);

        var facts = JsonNode.Parse(text)!.AsObject();
        facts["result"]!.GetValue<string>().Should().Be("PriceMismatch");
        facts["invoiceNumber"]!.GetValue<string>().Should().Be(invoiceNumber);
        facts["carrierBookingReference"]!.GetValue<string>().Should().Be(reference);
        facts["invoicedAmount"]!.GetValue<decimal>().Should().Be(95m);
        facts["agreedCarrierCost"]!.GetValue<decimal>().Should().Be(80m);
        text.Should().NotContain(FinanceAgentCalls.SpotRemark);
        text.Should().NotContainAny(FinanceAgentCalls.NeverForTheAgent);
        text.Should().NotContainEquivalentOf("Spot").And.NotContainEquivalentOf("remark");
        facts.Select(pair => pair.Key).Should().BeSubsetOf(RejectionReasonCalls.AllowedProperties);
    }

    [Fact]
    public async Task ALineWhoseCostIsFromARateCard_ShowsTheAgreedAmount_ButNotTheRateCardOrItsVersion()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 120m, _cancellationToken, rateCard: true);
        var (invoiceNumber, received) = await ReceiveAsync(reference, 118.5m);

        var text = await RejectionReasonCalls.ReadFactsTextAsync(fixture, FinanceAgentCalls.Carrier, invoiceNumber, reference, CorrelationOf(received), _cancellationToken);

        var facts = JsonNode.Parse(text)!.AsObject();
        facts["agreedCarrierCost"]!.GetValue<decimal>().Should().Be(120m);
        text.Should().NotContain("CRC-0001").And.NotContainEquivalentOf("rate card").And.NotContainEquivalentOf("rateCard").And.NotContainEquivalentOf("version");
        facts.Select(pair => pair.Key).Should().BeSubsetOf(RejectionReasonCalls.AllowedProperties);
    }

    [Fact]
    public async Task AnUnknownBookingLine_ShowsTheLikelyCorrectReference_WhenItIsOneOfTheCarriersOwnBookings()
    {
        var reference = FinanceAgentCalls.NewReference();
        await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken, reference);
        var mangled = reference.Replace("-", string.Empty, StringComparison.Ordinal);
        var (invoiceNumber, received) = await ReceiveAsync(mangled, 100m);

        var facts = await RejectionReasonCalls.ReadFactsAsync(fixture, FinanceAgentCalls.Carrier, invoiceNumber, mangled, CorrelationOf(received), _cancellationToken);

        facts["result"]!.GetValue<string>().Should().Be("UnknownBooking");
        facts["likelyCorrectReference"]!.GetValue<string>().Should().Be(reference);
        facts["agreedCarrierCost"].Should().BeNull("there is no booking under the reference the Carrier sent");
    }

    [Fact]
    public async Task AnUnknownBookingLine_NeverShowsAnotherCarriersReference_AsTheLikelyCorrectOne()
    {
        var reference = FinanceAgentCalls.NewReference();
        var mangled = reference.Replace("-", string.Empty, StringComparison.Ordinal);
        var otherCarriersReference = mangled + "Q";
        await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken, otherCarriersReference, FinanceAgentCalls.OtherCarrier);
        var (invoiceNumber, received) = await ReceiveAsync(mangled, 100m);

        var text = await RejectionReasonCalls.ReadFactsTextAsync(fixture, FinanceAgentCalls.Carrier, invoiceNumber, mangled, CorrelationOf(received), _cancellationToken);

        var facts = JsonNode.Parse(text)!.AsObject();
        facts["result"]!.GetValue<string>().Should().Be("UnknownBooking");
        facts["likelyCorrectReference"].Should().BeNull("the only likely booking is another Carrier's");
        text.Should().NotContain(otherCarriersReference);

        // Control: the same line from the other Carrier does get that reference, so it was a likely one and only the Carrier boundary kept it out above.
        var otherInvoice = FinanceAgentCalls.NewInvoiceNumber();
        var otherReceived = await FinanceAgentCalls.ReceiveAsync(fixture, otherInvoice, _cancellationToken, FinanceAgentCalls.OtherCarrier, (mangled, 100m));
        var otherFacts = await RejectionReasonCalls.ReadFactsAsync(fixture, FinanceAgentCalls.OtherCarrier, otherInvoice, mangled, CorrelationOf(otherReceived), _cancellationToken);
        otherFacts["likelyCorrectReference"]!.GetValue<string>().Should().Be(otherCarriersReference);
    }

    [Fact]
    public async Task AnUnknownBookingLine_WithBothCarriersBookingsLikely_ShowsOnlyTheCarriersOwn()
    {
        var reference = FinanceAgentCalls.NewReference();
        var mangled = reference.Replace("-", string.Empty, StringComparison.Ordinal);
        var otherCarriersReference = mangled + "Q";
        await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken, otherCarriersReference, FinanceAgentCalls.OtherCarrier);
        await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken, reference);
        var (invoiceNumber, received) = await ReceiveAsync(mangled, 100m);

        var text = await RejectionReasonCalls.ReadFactsTextAsync(fixture, FinanceAgentCalls.Carrier, invoiceNumber, mangled, CorrelationOf(received), _cancellationToken);

        JsonNode.Parse(text)!["likelyCorrectReference"]!.GetValue<string>().Should().Be(reference);
        text.Should().NotContain(otherCarriersReference);
    }

    [Fact]
    public async Task ADuplicateLine_ShowsTheCarriersOwnEarlierInvoiceNumber()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken);
        var (earlier, _) = await ReceiveAsync(reference, 100m);
        var (invoiceNumber, received) = await ReceiveAsync(reference, 100m);

        var facts = await RejectionReasonCalls.ReadFactsAsync(fixture, FinanceAgentCalls.Carrier, invoiceNumber, reference, CorrelationOf(received), _cancellationToken);

        facts["result"]!.GetValue<string>().Should().Be("Duplicate");
        facts["earlierInvoiceNumber"]!.GetValue<string>().Should().Be(earlier);
        facts.Select(pair => pair.Key).Should().BeSubsetOf(RejectionReasonCalls.AllowedProperties);
    }

    private async Task<(string InvoiceNumber, JsonObject Received)> ReceiveAsync(string reference, decimal amount)
    {
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        return (invoiceNumber, await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (reference, amount)));
    }

    private static string CorrelationOf(JsonObject received) => received["correlationId"]!.GetValue<string>();
}
