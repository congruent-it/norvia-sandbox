using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(105.145, 105.15)]
    [InlineData(105.144, 105.14)]
    [InlineData(112.902, 112.90)]
    public void RoundedToCents_Amount_RoundsHalfAwayFromZero(decimal amount, decimal rounded)
    {
        new Money(amount).RoundedToCents().Should().Be(new Money(rounded));
    }

    [Fact]
    public void Times_Factor_MultipliesTheAmountUnrounded()
    {
        new Money(12.37m).Times(8.5m).Should().Be(new Money(105.145m));
    }

    [Fact]
    public void Sum_Amounts_AddsThemUp()
    {
        Money.Sum([new Money(100.10m), new Money(4.90m)]).Should().Be(new Money(105.00m));
    }

    [Fact]
    public void Sum_Nothing_IsZero()
    {
        Money.Sum([]).Should().Be(new Money(0m));
    }

    [Fact]
    public void Currency_Always_IsEuros()
    {
        Money.Currency.Should().Be("EUR");
    }
}
