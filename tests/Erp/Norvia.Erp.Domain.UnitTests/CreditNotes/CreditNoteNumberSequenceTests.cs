using Norvia.Erp.Domain.CreditNotes;

namespace Norvia.Erp.Domain.UnitTests.CreditNotes;

public sealed class CreditNoteNumberSequenceTests
{
    [Fact]
    public void TakeNext_OnANewSequence_StartsAtOne()
    {
        var sequence = new CreditNoteNumberSequence(2026);

        sequence.TakeNext().Value.Should().Be("CN-2026-000001");
        sequence.LastTaken.Should().Be(1);
    }

    [Fact]
    public void TakeNext_Repeatedly_CountsWithoutGaps()
    {
        var sequence = new CreditNoteNumberSequence(2026);

        var numbers = Enumerable.Range(0, 3).Select(_ => sequence.TakeNext().Value);

        numbers.Should().Equal("CN-2026-000001", "CN-2026-000002", "CN-2026-000003");
    }

    [Fact]
    public void TakeNext_WhenSixDigitsAreUsedUp_Throws()
    {
        var sequence = new CreditNoteNumberSequence(2026);
        typeof(CreditNoteNumberSequence).GetProperty(nameof(CreditNoteNumberSequence.LastTaken))!.SetValue(sequence, CreditNoteNumberSequence.MaxPerYear);

        var take = sequence.TakeNext;

        take.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Year_IsTheKey() => new CreditNoteNumberSequence(2027).Year.Should().Be(2027);

    [Fact]
    public void CreditNoteNumber_OfShortYearAndSequence_PadsTheDigits() =>
        CreditNoteNumber.Of(2026, 42).Value.Should().Be("CN-2026-000042");

    [Fact]
    public void CompareTo_NumbersOfOneYear_OrdersBySequence()
    {
        (CreditNoteNumber.Of(2026, 9) < CreditNoteNumber.Of(2026, 10)).Should().BeTrue();
        (CreditNoteNumber.Of(2026, 10) >= CreditNoteNumber.Of(2026, 10)).Should().BeTrue();
        (CreditNoteNumber.Of(2026, 10) > CreditNoteNumber.Of(2026, 9)).Should().BeTrue();
        (CreditNoteNumber.Of(2026, 9) <= CreditNoteNumber.Of(2026, 9)).Should().BeTrue();
        CreditNoteNumber.Of(2026, 1).CompareTo((object)"x").Should().Be(1);
        CreditNoteNumber.Of(2026, 1).ToString().Should().Be("CN-2026-000001");
    }
}
