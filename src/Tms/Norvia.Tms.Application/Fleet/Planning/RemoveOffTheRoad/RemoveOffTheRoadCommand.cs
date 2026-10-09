using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.RemoveOffTheRoad;

/// <summary>The Planner removes an Off the road day (P10): the Vehicle is available again that day. A day before today is closed and stays.</summary>
public sealed record RemoveOffTheRoadCommand(OffTheRoadDayId Id) : ICommand<Result>;
