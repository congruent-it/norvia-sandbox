using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.SendAgain;

/// <summary>The Planner sends a Not delivered version of a Trip to the Driver app again (P11), under a new correlation ID.</summary>
public sealed record SendTripAgainCommand(string Number, int Version, string CorrelationId) : ICommand<Result<SendTripAgainResult>>;
