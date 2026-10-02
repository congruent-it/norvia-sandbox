using Mediator;

namespace Norvia.PartnerPortal.Application.CarrierBookings.AutoAnswer;

/// <summary>
/// Answers every open booking request whose automatic answer is due, each through the same answer use case as the
/// Confirm and Reject forms, so each goes through API Management to Norvia.
/// </summary>
public sealed record AnswerDueCarrierBookingsCommand : ICommand<AutoAnswerOutcome>;

/// <param name="NextDueAt">When the next automatic answer is due; null when no open request has one.</param>
public sealed record AutoAnswerOutcome(DateTimeOffset? NextDueAt);
