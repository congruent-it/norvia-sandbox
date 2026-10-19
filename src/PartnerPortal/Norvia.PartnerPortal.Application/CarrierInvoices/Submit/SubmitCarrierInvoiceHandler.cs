using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierInvoices.Submit;

public sealed class SubmitCarrierInvoiceHandler(
    IRepository<CarrierInvoice> repository,
    INorviaPartnerApi partnerApi,
    IPersonaDirectory personas,
    TimeProvider time)
    : ICommandHandler<SubmitCarrierInvoiceCommand, Result<SubmittedCarrierInvoice>>
{
    /// <summary>The longest invoice number the Carrier invoice format takes.</summary>
    public const int MaxInvoiceNumberLength = 50;

    public async ValueTask<Result<SubmittedCarrierInvoice>> Handle(SubmitCarrierInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (personas.Find(command.PersonaId) is not { PartyType: PartyType.Carrier } carrier)
        {
            return Result.NotFound($"There is no Carrier Persona '{command.PersonaId}'.");
        }

        if (Invalidity(command) is { } invalid)
        {
            return Result.Invalid(new ValidationError(invalid));
        }

        var number = command.InvoiceNumber.Trim();
        var reply = await partnerApi.SubmitCarrierInvoiceAsync(carrier, number, command.InvoiceDate, command.Lines, cancellationToken);
        if (reply is not { Result: CarrierInvoiceReplyResult.Received, Receipt: { } receipt })
        {
            return new SubmittedCarrierInvoice(number, reply, Invoice: null);
        }

        // The invoice number is the key: Norvia saying it has the invoice already, which the portal kept, adds nothing.
        var kept = await repository.FirstOrDefaultAsync(new CarrierInvoiceByNumberSpec(carrier.Id, number), cancellationToken);
        if (kept is null)
        {
            var descriptions = command.Lines.ToDictionary(line => line.BookingRef.Trim(), line => line.Description, StringComparer.Ordinal);
            kept = CarrierInvoice.Receive(
                carrier.Id, number, command.InvoiceDate, reply.MessageId ?? "unknown", time.GetUtcNow(), receipt.Status, receipt.ApprovedAmount,
                [.. receipt.Lines.Select(line => new CarrierInvoiceLine(line.BookingRef, line.Amount, descriptions.GetValueOrDefault(line.BookingRef), line.Result))]);
            await repository.AddAsync(kept, cancellationToken);
        }

        return new SubmittedCarrierInvoice(number, reply, CarrierInvoiceView.From(kept, outcome: null));
    }

    private static string? Invalidity(SubmitCarrierInvoiceCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.InvoiceNumber) || command.InvoiceNumber.Trim().Length > MaxInvoiceNumberLength)
        {
            return $"A Carrier invoice needs an invoice number of 1 to {MaxInvoiceNumberLength} characters.";
        }

        if (command.Lines.Count == 0)
        {
            return "A Carrier invoice needs at least one line.";
        }

        if (command.Lines.Any(line => string.IsNullOrWhiteSpace(line.BookingRef)))
        {
            return "Every line needs a booking reference.";
        }

        if (command.Lines.GroupBy(line => line.BookingRef.Trim(), StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            return "A booking reference can appear only once in an invoice.";
        }

        if (command.Lines.Any(line => line.Amount <= 0 || decimal.Round(line.Amount, 2) != line.Amount))
        {
            return "Every amount must be above zero, to the cent.";
        }

        return null;
    }
}
