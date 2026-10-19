using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierInvoices.Submit;

/// <summary>
/// Sends a Carrier invoice as a Carrier Persona to Norvia, through the Partner API, and keeps it when Norvia received it.
/// Not found when there is no such Carrier Persona; Invalid when the number is blank or over 50 characters, there is no
/// line, a booking reference is blank or repeated, or an amount is not above zero to the cent.
/// </summary>
public sealed record SubmitCarrierInvoiceCommand(
    PersonaId PersonaId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    IReadOnlyList<CarrierInvoiceLineDraft> Lines)
    : ICommand<Result<SubmittedCarrierInvoice>>;

/// <param name="BookingRef">The Carrier's own booking reference.</param>
/// <param name="Amount">What the Carrier claims, in EUR without VAT; the presenter may edit it to show a mismatch.</param>
public sealed record CarrierInvoiceLineDraft(string BookingRef, decimal Amount, string? Description = null);

/// <param name="Reply">What the Partner API answered.</param>
/// <param name="Invoice">The invoice as the Persona now keeps it, with the result of each line; null unless Norvia received it.</param>
public sealed record SubmittedCarrierInvoice(string InvoiceNumber, CarrierInvoiceReply Reply, CarrierInvoiceView? Invoice);
