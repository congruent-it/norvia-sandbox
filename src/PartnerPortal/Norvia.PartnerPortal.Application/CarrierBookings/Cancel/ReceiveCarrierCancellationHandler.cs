using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.Cancel;

public sealed class ReceiveCarrierCancellationHandler(
    IRepository<CarrierBookingRequest> repository,
    IPersonaDirectory personas,
    TimeProvider time)
    : ICommandHandler<ReceiveCarrierCancellationCommand, Result<ReceiveCarrierCancellationResult>>
{
    public async ValueTask<Result<ReceiveCarrierCancellationResult>> Handle(ReceiveCarrierCancellationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var carrier = personas.All.FirstOrDefault(persona => persona.PartyType == PartyType.Carrier && persona.PartyId == command.PartyId);
        if (carrier is null)
        {
            return Result.NotFound($"There is no Carrier Persona for '{command.PartyId}'.");
        }

        if (command.Kind == CarrierCancellationKind.Cancelled && string.IsNullOrWhiteSpace(command.ReasonText))
        {
            return Result.Invalid(new ValidationError(nameof(command.ReasonText), "A cancellation needs a reason."));
        }

        // The Carrier can't refuse, so a booking the inbox never had is acknowledged too: there is nothing to end.
        var request = await repository.FirstOrDefaultAsync(new CarrierBookingRequestToAnswerSpec(carrier.Id, command.CarrierBookingNumber), cancellationToken);
        if (request is null)
        {
            return new ReceiveCarrierCancellationResult(command.CarrierBookingNumber, Applied: false, Repeated: false);
        }

        // The Carrier booking number is the key: a repeat finds the booking already ended and is not an error.
        var repeated = request.Status == (command.Kind == CarrierCancellationKind.Cancelled
            ? CarrierBookingRequestStatus.CancelledByNorvia
            : CarrierBookingRequestStatus.WithdrawnByNorvia);
        var applied = !repeated && (command.Kind == CarrierCancellationKind.Cancelled
            ? request.CancelledByNorvia(command.ReasonText!, time.GetUtcNow())
            : request.WithdrawnByNorvia(command.CorrelationId, time.GetUtcNow()));
        if (applied)
        {
            await repository.UpdateAsync(request, cancellationToken);
        }

        return new ReceiveCarrierCancellationResult(command.CarrierBookingNumber, applied, repeated);
    }
}
