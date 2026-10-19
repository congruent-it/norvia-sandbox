using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierInvoices.Outcome;

public sealed class ReceiveCarrierInvoiceOutcomeHandler(IRepository<CarrierInvoiceOutcome> repository, IPersonaDirectory personas, TimeProvider time)
    : ICommandHandler<ReceiveCarrierInvoiceOutcomeCommand, Result<ReceiveCarrierInvoiceOutcomeResult>>
{
    public async ValueTask<Result<ReceiveCarrierInvoiceOutcomeResult>> Handle(ReceiveCarrierInvoiceOutcomeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var carrier = personas.All.FirstOrDefault(persona => persona.PartyType == PartyType.Carrier && persona.PartyId == command.PartyId);
        if (carrier is null)
        {
            return Result.NotFound($"There is no Carrier Persona for '{command.PartyId}'.");
        }

        // The invoice number is the key: an outcome told again is the one the Persona has, and is not an error.
        if (await repository.AnyAsync(new CarrierInvoiceOutcomeByNumberSpec(carrier.Id, command.InvoiceNumber), cancellationToken))
        {
            return new ReceiveCarrierInvoiceOutcomeResult(command.InvoiceNumber, Repeated: true);
        }

        await repository.AddAsync(
            CarrierInvoiceOutcome.Receive(carrier.Id, command.InvoiceNumber, command.CorrelationId, time.GetUtcNow(), command.ApprovedAmount, command.Lines),
            cancellationToken);
        return new ReceiveCarrierInvoiceOutcomeResult(command.InvoiceNumber, Repeated: false);
    }
}
