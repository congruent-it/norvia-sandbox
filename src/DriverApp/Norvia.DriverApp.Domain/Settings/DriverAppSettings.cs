using Ardalis.SharedKernel;

namespace Norvia.DriverApp.Domain.Settings;

/// <summary>The demo settings of the Driver app, one row for the whole app.</summary>
public sealed class DriverAppSettings : EntityBase<int>, IAggregateRoot
{
    public const int SingleId = 1;

    public DriverAppSettings() => Id = SingleId;

    /// <summary>The "Driver app unavailable" demo fault: while on, the Driver app refuses every Trip version with a 503.</summary>
    public bool Unavailable { get; private set; }

    public void SetUnavailable(bool unavailable) => Unavailable = unavailable;
}
