using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.UnitTests.CarrierCosts;

/// <summary>The ERP ranks the likely candidates of an Unknown booking line, never the model (ADR 0040).</summary>
public sealed class CandidateCarrierCostsTests
{
    private static readonly DateOnly InvoiceDate = new(2026, 10, 19);
    private static readonly CarrierId Carrier = new("CAR-0001");

    private static CarrierCost Cost(string number, string reference, decimal total, DateTimeOffset? delivered = null, bool invoiced = false)
    {
        var cost = CarrierCost.Record(
            new CarrierCostData(
                number, reference, Carrier, "SH-0000001", "TO-0000001",
                new CarrierCostCharge(total, [new ChargeLine(ChargeLineType.Freight, total)]), CarrierCostSource.FromSpot("r"),
                delivered ?? new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero), "corr"),
            new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero)).Cost!;
        if (invoiced)
        {
            cost.MarkInvoiced();
        }

        return cost;
    }

    [Fact]
    public void Rank_AMangledReference_FindsTheCostWhoseReferenceItIs()
    {
        var wanted = Cost("CB-1", "TRX-0042", 80m);

        var ranked = CandidateCarrierCosts.Rank("trx 0042", 80m, InvoiceDate, [Cost("CB-2", "ABC-9999", 3000m), wanted]);

        ranked.Select(candidate => candidate.Cost).Should().Equal(wanted);
        ranked[0].Score.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void Rank_ReferencesThatAreAlike_OutrankOnesThatAreNot_AtTheSameAmount()
    {
        var close = Cost("CB-1", "TRX-0042", 80m);
        var farther = Cost("CB-2", "TRX-0099", 80m);
        var farthest = Cost("CB-3", "PO-77", 80m);

        var ranked = CandidateCarrierCosts.Rank("TRX-0043", 80m, InvoiceDate, [farthest, farther, close]);

        ranked.Select(candidate => candidate.Cost.Id.CarrierBookingNumber).Should().ContainInOrder("CB-1", "CB-2");
        ranked[0].Score.Should().BeGreaterThan(ranked[1].Score);
    }

    [Fact]
    public void Rank_ACostWithTheSameAmountButAnUnrelatedReference_IsStillALikelyCandidate()
    {
        var sameAmount = Cost("CB-1", "ZZ", 80m);

        CandidateCarrierCosts.Rank("TRX-0042", 80m, InvoiceDate, [sameAmount]).Select(candidate => candidate.Cost).Should().Equal(sameAmount);
    }

    [Fact]
    public void Rank_NothingThatLooksLikeIt_GivesNoCandidate()
    {
        CandidateCarrierCosts.Rank("TRX-0042", 80m, InvoiceDate, [Cost("CB-1", "ZZ", 5000m)]).Should().BeEmpty();
    }

    [Fact]
    public void Rank_OffersAtMostThreeCandidates_BestFirst()
    {
        var costs = Enumerable.Range(1, 6).Select(index => Cost($"CB-{index}", $"TRX-004{index}", 80m)).ToList();

        var ranked = CandidateCarrierCosts.Rank("TRX-0042", 80m, InvoiceDate, costs);

        ranked.Should().HaveCount(CandidateCarrierCosts.MaxCandidates);
        ranked.Select(candidate => candidate.Score).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Rank_AnInvoicedCost_IsNeverACandidate()
    {
        CandidateCarrierCosts.Rank("TRX-0042", 80m, InvoiceDate, [Cost("CB-1", "TRX-0042", 80m, invoiced: true)]).Should().BeEmpty();
    }

    [Fact]
    public void Rank_ACostDeliveredAfterTheInvoiceDate_RanksBelowOneDeliveredBefore_AllElseEqual()
    {
        var before = Cost("CB-1", "TRX-0042", 80m, new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero));
        var after = Cost("CB-2", "TRX-0042", 80m, new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero));

        CandidateCarrierCosts.Rank("TRX-0042", 80m, InvoiceDate, [after, before]).Select(candidate => candidate.Cost.Id.CarrierBookingNumber).Should().Equal("CB-1", "CB-2");
    }

    [Fact]
    public void Rank_EqualScores_AreOrderedByBookingNumber_SoTheRankingIsStable()
    {
        var ranked = CandidateCarrierCosts.Rank("TRX-0042", 80m, InvoiceDate, [Cost("CB-2", "TRX-0042", 80m), Cost("CB-1", "TRX-0042", 80m)]);

        ranked.Select(candidate => candidate.Cost.Id.CarrierBookingNumber).Should().Equal("CB-1", "CB-2");
    }

    [Theory]
    [InlineData("TRX-0042", "trx 0042", 1d)]
    [InlineData("", "", 0d)]
    [InlineData("ABC", "XYZ", 0d)]
    public void ReferenceSimilarity_IgnoresCaseSpacesAndPunctuation(string left, string right, double expected)
    {
        CandidateCarrierCosts.ReferenceSimilarity(left, right).Should().BeApproximately(expected, 0.0001);
    }

    [Theory]
    [InlineData(80, 80, 1d)]
    [InlineData(80, 40, 0.5d)]
    [InlineData(0, 0, 1d)]
    [InlineData(10, 1000, 0.01d)]
    public void AmountCloseness_FallsAsTheAmountsGrowApart(double invoiced, double agreed, double expected)
    {
        CandidateCarrierCosts.AmountCloseness((decimal)invoiced, (decimal)agreed).Should().BeApproximately(expected, 0.0001);
    }

    [Fact]
    public void Specs_OpenCostsOfCarrier_KeepsOnlyOpenCostsOfThatCarrier_AndInvoicesOfCarrier_OnlyThatCarriers()
    {
        var open = Cost("CB-1", "A", 1m);
        var invoiced = Cost("CB-2", "B", 1m, invoiced: true);

        new OpenCarrierCostsOfCarrierSpec(Carrier).Evaluate([open, invoiced]).Should().Equal(open);
        new OpenCarrierCostsOfCarrierSpec(new CarrierId("CAR-0002")).Evaluate([open, invoiced]).Should().BeEmpty();
    }
}
