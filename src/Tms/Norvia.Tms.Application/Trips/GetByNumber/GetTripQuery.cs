using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.GetByNumber;

/// <summary>One Trip with its Stops in order, load, rule failures and warnings, for the Planner's Trip detail (P11).</summary>
public sealed record GetTripQuery(string Number) : IQuery<Result<TripDetailView>>;
