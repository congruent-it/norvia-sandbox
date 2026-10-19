using System.Text.Json;
using Norvia.Simulator.Domain.Invoicing;

namespace Norvia.Simulator.Domain.UnitTests.Invoicing;

public sealed class InvoicingFunnelTests
{
    private static IReadOnlyList<InvoicingJourney> Plan(InvoicingShares shares, int count = 10, int seed = 7) => InvoicingFunnel.Plan(shares, seed, count);

    [Fact]
    public void Plan_GivesOneJourneyPerShipper_InOrder() =>
        Plan(new InvoicingShares(), count: 6).Select(journey => journey.Index).Should().Equal(Enumerable.Range(0, 6));

    [Fact]
    public void Plan_NoShippers_GivesNoJourneys() => Plan(new InvoicingShares(), count: 0).Should().BeEmpty();

    [Fact]
    public void Plan_NothingInvoiced_CreditsNothingEither() =>
        Plan(new InvoicingShares(0, 100)).Should().OnlyContain(journey => !journey.Invoice && !journey.CreditNote);

    [Fact]
    public void Plan_EveryShipperInvoicedAndCredited_DoesBoth() =>
        Plan(new InvoicingShares(100, 100)).Should().OnlyContain(journey => journey.Invoice && journey.CreditNote);

    [Fact]
    public void Plan_ExactlyTheSharesAsked()
    {
        var counts = InvoicingFunnel.Count(Plan(new InvoicingShares(60, 50), count: 10));

        counts.Should().Be(new InvoicingFunnelCounts(10, 6, 3));
    }

    [Fact]
    public void Plan_ACreditNote_IsOnlyEverOnAnInvoice()
    {
        foreach (var seed in Enumerable.Range(1, 20))
        {
            Plan(new InvoicingShares(40, 80), count: 9, seed: seed).Where(journey => journey.CreditNote).Should().OnlyContain(journey => journey.Invoice);
        }
    }

    [Fact]
    public void Plan_TheSameSeed_GivesTheSameJourneys_AndAnotherSeedOtherOnes()
    {
        var shares = new InvoicingShares(50, 50);

        JsonSerializer.Serialize(Plan(shares, count: 30, seed: 42)).Should().Be(JsonSerializer.Serialize(Plan(shares, count: 30, seed: 42)));
        JsonSerializer.Serialize(Plan(shares, count: 30, seed: 1)).Should().NotBe(JsonSerializer.Serialize(Plan(shares, count: 30, seed: 2)));
    }

    [Theory]
    [InlineData(-1, 50, "invoices")]
    [InlineData(101, 50, "invoices")]
    [InlineData(50, -1, "credits")]
    [InlineData(50, 101, "credits")]
    public void Problems_ASharePercentOutOfRange_IsNamed(int invoice, int credit, string named) =>
        new InvoicingShares(invoice, credit).Problems().Should().ContainSingle().Which.Should().Contain(named).And.Contain("0 to 100");

    [Fact]
    public void Problems_TheDefaults_AreFine() => new InvoicingShares().Problems().Should().BeEmpty();
}
