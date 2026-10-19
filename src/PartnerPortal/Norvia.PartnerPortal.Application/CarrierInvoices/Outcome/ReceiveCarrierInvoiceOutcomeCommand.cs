using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierInvoices;

namespace Norvia.PartnerPortal.Application.CarrierInvoices.Outcome;

/// <summary>
/// Keeps the outcome Norvia told a Carrier Persona about a Carrier invoice, unless the Persona has an outcome for that
/// invoice number already. It only acknowledges: nothing is sent back to Norvia but the answer.
/// </summary>
/// <param name="PartyId">The Carrier's ID at Norvia, such as <c>CAR-0001</c>, which names the Persona.</param>
public sealed record ReceiveCarrierInvoiceOutcomeCommand(
    string PartyId,
    string CorrelationId,
    string InvoiceNumber,
    decimal ApprovedAmount,
    IReadOnlyList<CarrierInvoiceOutcomeLine> Lines) : ICommand<Result<ReceiveCarrierInvoiceOutcomeResult>>;

/// <param name="Repeated">True when the Persona had the outcome already, so nothing was added.</param>
public sealed record ReceiveCarrierInvoiceOutcomeResult(string InvoiceNumber, bool Repeated);
