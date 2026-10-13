namespace Norvia.Simulator.Application.Ports;

/// <summary>Where a new random seed comes from, so the screens can offer one and tests don't depend on chance.</summary>
public interface ISeedSource
{
    int NewSeed();
}
