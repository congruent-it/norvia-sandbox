namespace Norvia.Simulator.Domain;

/// <summary>
/// A small generator that gives the same numbers for the same seed on every machine and .NET version, which
/// <see cref="Random"/> does not promise. It is SplitMix64.
/// </summary>
public sealed class SeededRandom(int seed)
{
    private ulong _state = (ulong)(uint)seed;

    /// <summary>A number from 0 up to, not including, <paramref name="maxExclusive"/>.</summary>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, 1);
        return (int)(NextUInt64() % (ulong)maxExclusive);
    }

    /// <summary>A number from <paramref name="minInclusive"/> to <paramref name="maxInclusive"/>.</summary>
    public int Between(int minInclusive, int maxInclusive) => minInclusive + Next(maxInclusive - minInclusive + 1);

    public T Pick<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items[Next(items.Count)];
    }

    /// <summary>The numbers 0 to <paramref name="count"/> - 1 in an order that depends on the seed.</summary>
    public int[] Shuffled(int count)
    {
        var numbers = Enumerable.Range(0, count).ToArray();
        for (var i = count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (numbers[i], numbers[j]) = (numbers[j], numbers[i]);
        }

        return numbers;
    }

    private ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
