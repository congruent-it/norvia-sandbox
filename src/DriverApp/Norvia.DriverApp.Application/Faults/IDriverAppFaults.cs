namespace Norvia.DriverApp.Application.Faults;

/// <summary>The Driver app's runtime faults, which the presenter switches on for a few minutes.</summary>
public interface IDriverAppFaults
{
    /// <summary>Whether the "Driver app unavailable" fault is on now.</summary>
    bool IsUnavailable { get; }
}
