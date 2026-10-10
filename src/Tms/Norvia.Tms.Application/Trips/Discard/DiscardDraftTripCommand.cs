using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.Discard;

/// <summary>The Planner discards a Draft Trip (P11), which gives its Vehicle and Driver back. A Dispatched Trip is never deleted. Nothing is published.</summary>
public sealed record DiscardDraftTripCommand(string Number) : ICommand<Result>;
