namespace Norvia.ServiceDefaults.Faults;

public enum SwitchOnResult
{
    /// <summary>The fault is on until its expiry.</summary>
    Switched,

    /// <summary>The application owns no fault of that name.</summary>
    UnknownFault,

    /// <summary>The duration is zero, negative or longer than <see cref="FaultOptions.MaxDuration"/>.</summary>
    DurationNotAllowed,
}
