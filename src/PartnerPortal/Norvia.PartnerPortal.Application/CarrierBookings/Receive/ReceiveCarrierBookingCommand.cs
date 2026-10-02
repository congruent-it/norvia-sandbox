using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierBookings.Receive;

/// <summary>Puts a booking request Norvia sent a Carrier in the Carrier's inbox, unless the inbox has it already.</summary>
/// <param name="PartyId">The Carrier's ID at Norvia, such as <c>CAR-0001</c>, which names the Persona whose inbox it is.</param>
public sealed record ReceiveCarrierBookingCommand(
    string PartyId,
    string CarrierBookingNumber,
    string ShipmentNumber,
    string CorrelationId,
    CarrierBookingDetails Details) : ICommand<Result<ReceiveCarrierBookingResult>>;

/// <param name="Repeated">True when the inbox had the request already, so nothing was added.</param>
public sealed record ReceiveCarrierBookingResult(string CarrierBookingNumber, bool Repeated);
