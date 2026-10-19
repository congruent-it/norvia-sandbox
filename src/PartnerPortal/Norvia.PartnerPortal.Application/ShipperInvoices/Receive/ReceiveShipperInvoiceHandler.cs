using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Application.ShipperInvoices.Receive;

public sealed class ReceiveShipperInvoiceHandler(IRepository<ShipperInvoice> repository, IPersonaDirectory personas, TimeProvider time)
    : ICommandHandler<ReceiveShipperInvoiceCommand, Result<ReceiveShipperInvoiceResult>>
{
    public async ValueTask<Result<ReceiveShipperInvoiceResult>> Handle(ReceiveShipperInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipper = personas.All.FirstOrDefault(persona => persona.PartyType == PartyType.Shipper && persona.PartyId == command.PartyId);
        if (shipper is null)
        {
            return Result.NotFound($"There is no Shipper Persona for '{command.PartyId}'.");
        }

        // The document number is the key: a repeated document is the one the inbox has, and is not an error.
        var documentNumber = command.Content.DocumentNumber;
        if (await repository.AnyAsync(new ShipperInvoiceByDocumentNumberSpec(shipper.Id, documentNumber), cancellationToken))
        {
            return new ReceiveShipperInvoiceResult(documentNumber, Repeated: true);
        }

        await repository.AddAsync(ShipperInvoice.Receive(shipper.Id, command.CorrelationId, time.GetUtcNow(), command.Content), cancellationToken);
        return new ReceiveShipperInvoiceResult(documentNumber, Repeated: false);
    }
}
