using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierBookings.Cancel;

/// <summary>Applies what Norvia sent a Carrier Persona about a booking it ended, once per Carrier booking number.</summary>
/// <param name="PartyId">The Carrier's ID at Norvia, such as <c>CAR-0001</c>, which names the Persona whose inbox it is.</param>
/// <param name="ReasonText">Why Norvia cancelled; required for a cancellation, absent for a withdrawal.</param>
public sealed record ReceiveCarrierCancellationCommand(
    string PartyId,
    string CarrierBookingNumber,
    CarrierCancellationKind Kind,
    string? ReasonText,
    string CorrelationId) : ICommand<Result<ReceiveCarrierCancellationResult>>;
