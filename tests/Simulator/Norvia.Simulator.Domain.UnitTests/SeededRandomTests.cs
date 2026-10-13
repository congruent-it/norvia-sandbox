namespace Norvia.Simulator.Domain.UnitTests;

public sealed class SeededRandomTests
{
    [Fact]
    public void Next_TheSameSeed_GivesTheSameNumbers()
    {
        var first = new SeededRandom(123);
        var second = new SeededRandom(123);

        Enumerable.Range(0, 20).Select(_ => first.Next(1000)).Should().Equal(Enumerable.Range(0, 20).Select(_ => second.Next(1000)));
    }

    [Fact]
    public void Next_KnownSeed_GivesTheSameNumbersOnEveryMachine()
    {
        // SplitMix64 from seed 0: the first output is 0xE220A8397B1DCDAF, whose remainder modulo 1000 is 535.
        new SeededRandom(0).Next(1000).Should().Be(535);
    }

    [Fact]
    public void Between_StaysWithinBothBounds()
    {
        var random = new SeededRandom(5);

        Enumerable.Range(0, 200).Select(_ => random.Between(3, 6)).Should().OnlyContain(n => n >= 3 && n <= 6);
    }

    [Fact]
    public void Shuffled_HasEveryNumberOnce()
    {
        new SeededRandom(9).Shuffled(50).Order().Should().Equal(Enumerable.Range(0, 50));
    }

    [Fact]
    public void Next_NoRoom_Throws()
    {
        var act = () => new SeededRandom(1).Next(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
