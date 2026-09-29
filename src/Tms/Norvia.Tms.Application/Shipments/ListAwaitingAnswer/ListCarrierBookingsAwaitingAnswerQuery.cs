using Mediator;

namespace Norvia.Tms.Application.Shipments.ListAwaitingAnswer;

public sealed record ListCarrierBookingsAwaitingAnswerQuery : IQuery<IReadOnlyList<CarrierBookingAwaitingAnswerSummary>>;
