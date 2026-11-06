using System.Globalization;

namespace Norvia.Simulator.Domain.Personas;

/// <summary>
/// A pool of realistic free texts for one profile, category and language. The plan carries the pools and the run draws from them by the
/// seed, so the plan decides every outcome and a text is only the words inside one. Where the texts came from is kept with them.
/// </summary>
/// <param name="Source"><see cref="BuiltInSource"/> or <see cref="AgentSource"/>.</param>
public sealed record TextPool(string ProfileId, string Language, TextCategory Category, string Source, IReadOnlyList<string> Texts)
{
    public const string BuiltInSource = "built-in";

    // Apart from the plan's other random sequences.
    private const int PoolSalt = 0x504F4F4C;

    /// <summary>What the log and the preview call the pool: never a text.</summary>
    public string Name => $"{ProfileId}/{Category}/{Language}";

    public static string AgentSource(int runNumber) => string.Create(CultureInfo.InvariantCulture, $"agent, run {runNumber}");

    /// <summary>The text for one step of the run, picked by the seed: the same seed and step always draw the same one.</summary>
    public PoolDraw Draw(int seed, int step)
    {
        if (Texts.Count == 0)
        {
            throw new InvalidOperationException($"The pool {Name} has no text to draw.");
        }

        var random = new SeededRandom(unchecked(seed ^ PoolSalt ^ (step * 31) ^ ((int)Category * 7919)));
        var index = random.Next(Texts.Count);
        return new PoolDraw(this, index, Texts[index]);
    }
}

/// <summary>A text drawn from a pool, with where it was drawn from.</summary>
public sealed record PoolDraw(TextPool Pool, int Index, string Text)
{
    /// <summary>The pool and the index, which the log keeps instead of the text (coding guidelines §7).</summary>
    public string Reference => $"pool {Pool.Name}, index {Index}";
}
