using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Application.ShipperInvoices.Receive;

/// <summary>Puts an invoice or Credit note Norvia sent a Shipper in the Shipper's invoices inbox, unless the inbox has it already.</summary>
/// <param name="PartyId">The Shipper's ID at Norvia, such as <c>SHP-0001</c>, which names the Persona whose inbox it is.</param>
public sealed record ReceiveShipperInvoiceCommand(
    string PartyId,
    string CorrelationId,
    ShipperInvoiceContent Content) : ICommand<Result<ReceiveShipperInvoiceResult>>;

/// <param name="Repeated">True when the inbox had the document already, so nothing was added.</param>
public sealed record ReceiveShipperInvoiceResult(string DocumentNumber, bool Repeated);
