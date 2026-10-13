namespace Norvia.Simulator.Domain.Plans;

/// <summary>What Norvia is expected to answer to a planned booking.</summary>
public enum ExpectedAnswer
{
    Accepted,
    AlreadyBooked,
    Rejected,
}
