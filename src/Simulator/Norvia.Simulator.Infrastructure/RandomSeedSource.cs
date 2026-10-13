using Norvia.Simulator.Application.Ports;

namespace Norvia.Simulator.Infrastructure;

internal sealed class RandomSeedSource : ISeedSource
{
    public int NewSeed() => Random.Shared.Next(1, 1_000_000);
}
