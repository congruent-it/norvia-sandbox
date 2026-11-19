namespace Norvia.Docs.Diagrams.UnitTests;

public sealed class Mt19937Tests
{
    [Fact]
    public void NextSeed_SeededWith7_DrawsWhatThePythonGeneratorDrew()
    {
        // random.Random(7).randint(1, 2**31 - 1), the first four draws, then the 625th (past one twist) and the 1000th.
        var random = new Mt19937(7);
        var draws = Enumerable.Range(0, 1000).Select(_ => random.NextSeed()).ToList();

        draws.Take(4).Should().Equal(695425565, 2035525363, 323946140, 847877000);
        draws[624].Should().Be(346745721);
        draws[999].Should().Be(1399159378);
    }

    [Fact]
    public void NextSeed_AnySeed_StaysInTheRangeOfAnExcalidrawSeed()
    {
        var random = new Mt19937(12345);

        Enumerable.Range(0, 5000).Select(_ => random.NextSeed()).Should().OnlyContain(seed => seed >= 1);
    }
}
