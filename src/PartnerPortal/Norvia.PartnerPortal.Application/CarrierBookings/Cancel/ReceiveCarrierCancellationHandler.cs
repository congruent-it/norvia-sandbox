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
            return new ReceiveCarrierCancellationResult(command.CarrierBookingNumber, CarrierCancellationOutcome.NotApplicable);
        }

        // The Carrier booking number is the key: the booking decides whether this message is a repeat.
        var outcome = request.EndedByNorvia(command.Kind, command.ReasonText, command.CorrelationId, time.GetUtcNow());
        if (outcome == CarrierCancellationOutcome.Applied)
        {
            await repository.UpdateAsync(request, cancellationToken);
        }

        return new ReceiveCarrierCancellationResult(command.CarrierBookingNumber, outcome);
    }
}
