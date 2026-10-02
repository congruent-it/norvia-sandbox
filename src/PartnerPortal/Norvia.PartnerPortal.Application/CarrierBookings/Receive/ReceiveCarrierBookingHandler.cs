using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.Receive;

public sealed class ReceiveCarrierBookingHandler(IRepository<CarrierBookingRequest> repository, IPersonaDirectory personas, TimeProvider time)
    : ICommandHandler<ReceiveCarrierBookingCommand, Result<ReceiveCarrierBookingResult>>
{
    public async ValueTask<Result<ReceiveCarrierBookingResult>> Handle(ReceiveCarrierBookingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var carrier = personas.All.FirstOrDefault(persona => persona.PartyType == PartyType.Carrier && persona.PartyId == command.PartyId);
        if (carrier is null)
        {
            return Result.NotFound($"There is no Carrier Persona for '{command.PartyId}'.");
        }

        // The Carrier booking number is the key: a repeated request is the one the inbox has, and is not an error.
        if (await repository.AnyAsync(new CarrierBookingRequestByNumberSpec(carrier.Id, command.CarrierBookingNumber), cancellationToken))
        {
            return new ReceiveCarrierBookingResult(command.CarrierBookingNumber, Repeated: true);
        }

        var request = CarrierBookingRequest.Receive(
            carrier.Id, command.CarrierBookingNumber, command.ShipmentNumber, command.CorrelationId, time.GetUtcNow(), command.Details);
        await repository.AddAsync(request, cancellationToken);
        return new ReceiveCarrierBookingResult(command.CarrierBookingNumber, Repeated: false);
    }
}
