using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Domain.UnitTests.ShipperInvoices;

public sealed class InvoiceNumberSequenceTests
{
    [Fact]
    public void TakeNext_OnANewSequence_StartsAtOne()
    {
        var sequence = new InvoiceNumberSequence(2026);

        sequence.TakeNext().Value.Should().Be("INV-2026-000001");
        sequence.LastTaken.Should().Be(1);
    }

    [Fact]
    public void TakeNext_Repeatedly_CountsWithoutGaps()
    {
        var sequence = new InvoiceNumberSequence(2026);

        var numbers = Enumerable.Range(0, 3).Select(_ => sequence.TakeNext().Value);

        numbers.Should().Equal("INV-2026-000001", "INV-2026-000002", "INV-2026-000003");
    }

    [Fact]
    public void TakeNext_WhenSixDigitsAreUsedUp_Throws()
    {
        var sequence = new InvoiceNumberSequence(2026);
        typeof(InvoiceNumberSequence).GetProperty(nameof(InvoiceNumberSequence.LastTaken))!.SetValue(sequence, InvoiceNumberSequence.MaxPerYear);

        var take = sequence.TakeNext;

        take.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InvoiceNumber_OfShortYearAndSequence_PadsTheDigits() =>
        InvoiceNumber.Of(2026, 42).Value.Should().Be("INV-2026-000042");

    [Fact]
    public void CompareTo_NumbersOfOneYear_OrdersBySequence() =>
        (InvoiceNumber.Of(2026, 9) < InvoiceNumber.Of(2026, 10)).Should().BeTrue();
}
