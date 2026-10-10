using Mediator;

namespace Norvia.Tms.Application.Trips.Choices;

/// <summary>What the Planner can pick for a Trip on a day: the Vehicles and Drivers available then that hold no other Trip (P11). <paramref name="ForTrip"/> keeps what that Trip holds.</summary>
public sealed record ListTripChoicesQuery(DateOnly Day, string? ForTrip) : IQuery<TripChoices>;
