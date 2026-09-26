using Mediator;

namespace Norvia.Tms.Application.TransportOrders.RatingExceptions;

/// <summary>The rating exceptions (P2): every Transport Order whose Rating failed, with why, oldest first.</summary>
public sealed record ListRatingExceptionsQuery : IQuery<IReadOnlyList<RatingExceptionSummary>>;
