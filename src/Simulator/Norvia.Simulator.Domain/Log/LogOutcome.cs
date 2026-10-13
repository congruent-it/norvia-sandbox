namespace Norvia.Simulator.Domain.Log;

public enum LogOutcome
{
    /// <summary>A line of the Simulator itself: the run started, was cancelled or finished.</summary>
    Info,

    Accepted,
    AlreadyBooked,
    Rejected,

    /// <summary>Norvia refused the Persona's credentials.</summary>
    Refused,

    /// <summary>Nothing reached Norvia, or its answer couldn't be read.</summary>
    NotReceived,
}
