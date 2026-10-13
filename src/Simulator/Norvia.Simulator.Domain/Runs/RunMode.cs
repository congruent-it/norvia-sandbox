namespace Norvia.Simulator.Domain.Runs;

public enum RunMode
{
    /// <summary>Sent as fast as the estate accepts.</summary>
    Burst,

    /// <summary>Spread over the run's window.</summary>
    Paced,
}
