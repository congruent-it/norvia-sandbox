using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.UnitTests.Invoicing;

public sealed class CarrierInvoicingFunnelTests
{
    private static InvoicingShares Shares(int edited = 50, int rejected = 25) => new(100, 50, edited, rejected);

    [Fact]
    public void EditedLines_AShareOfTheInvoices_EditsExactlyThatShare_OnALineThatIsThere()
    {
        var lineCounts = new[] { 3, 1, 2, 4, 1, 2, 3, 1, 2, 2 };

        var edited = CarrierInvoicingFunnel.EditedLines(Shares(edited: 40), seed: 7, lineCounts);

        edited.Count(line => line is not null).Should().Be(4);
        edited.Zip(lineCounts).Where(pair => pair.First is not null).Should().OnlyContain(pair => pair.First >= 0 && pair.First < pair.Second);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 5)]
    public void EditedLines_NoneOrAll_EditsNoneOrEveryInvoice(int percent, int expected) =>
        CarrierInvoicingFunnel.EditedLines(Shares(edited: percent), seed: 3, [2, 2, 2, 2, 2]).Count(line => line is not null).Should().Be(expected);

    [Fact]
    public void EditedLines_TheSameSeed_GivesTheSameLines() =>
        CarrierInvoicingFunnel.EditedLines(Shares(), 9, [3, 3, 3, 3]).Should().Equal(CarrierInvoicingFunnel.EditedLines(Shares(), 9, [3, 3, 3, 3]));

    [Fact]
    public void EditedLines_NoInvoices_GivesNothing() => CarrierInvoicingFunnel.EditedLines(Shares(), 1, []).Should().BeEmpty();

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(25, 8, 2)]
    [InlineData(100, 6, 6)]
    public void RejectedLines_RejectsExactlyTheShareOfTheLinesLeftToDecide(int percent, int lines, int expected)
    {
        var rejected = CarrierInvoicingFunnel.RejectedLines(Shares(rejected: percent), seed: 5, lines);

        rejected.Should().HaveCount(expected).And.OnlyContain(position => position >= 0 && position < lines);
    }

    [Fact]
    public void RejectedLines_NothingLeftToDecide_RejectsNothing() => CarrierInvoicingFunnel.RejectedLines(Shares(rejected: 100), 5, 0).Should().BeEmpty();

    [Fact]
    public void RejectedLines_TheSameSeed_GivesTheSameLines() =>
        CarrierInvoicingFunnel.RejectedLines(Shares(rejected: 50), 4, 10).Should().BeEquivalentTo(CarrierInvoicingFunnel.RejectedLines(Shares(rejected: 50), 4, 10));

    [Theory]
    [InlineData(101, 0, "Carrier invoices with an edited amount")]
    [InlineData(0, -1, "Carrier invoice lines Finance rejects")]
    public void Problems_ACarrierShareOutOfRange_IsNamed(int edited, int rejected, string named) =>
        new InvoicingShares(100, 50, edited, rejected).Problems().Should().ContainSingle().Which.Should().Contain(named).And.Contain("0 to 100");

    [Fact]
    public void Defaults_AreHalfEditedAndAQuarterRejected() =>
        new InvoicingShares().Should().Be(new InvoicingShares(100, 50, 50, 25));

    [Fact]
    public void InvoiceNumber_IsTheCarriersPrefixWithTheRun()
    {
        var carrier = new SimulatedCarrier("CAR-0001", "Van Dijk Transport", true, "van-dijk-transport", "Van Dijk Transport", "VDT");

        CarrierReferences.InvoiceNumber(carrier, 7).Should().Be("VDT-SIM7-INV");
    }
}
