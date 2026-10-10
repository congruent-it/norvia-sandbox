using Ardalis.Result;
using Mediator;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Trips.Receive;

/// <summary>A Trip version, as the Trip dispatch Logic App pushes it.</summary>
public sealed record ReceiveTripVersionCommand(string TripNumber, int Version, string DriverId, TripDetails Details)
    : ICommand<Result<TripVersionOutcome>>;

public enum TripVersionOutcome
{
    /// <summary>The version is now the Trip's latest.</summary>
    Stored,

    /// <summary>The Driver app already held this version or a later one, so nothing changed.</summary>
    Ignored,
}
