using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The ERP's answer to Invoice now, as the log shows it. <see cref="Detail"/> holds a refusal's code, never the message the ERP words it with.
/// <see cref="InvoiceNumber"/> and <see cref="Lines"/> are there when an invoice was issued; <see cref="CorrelationId"/> is the invoice's own trail.
/// </summary>
public sealed record InvoiceNowAnswer(LogOutcome Outcome, string? CorrelationId, string? InvoiceNumber, IReadOnlyList<InvoicedLine> Lines, string? Detail, decimal TotalWithoutVat = 0);

/// <summary>A line of an issued invoice: the Transport Order it bills, the Shipper's own reference and the line total without VAT.</summary>
public sealed record InvoicedLine(string TransportOrderNumber, string ShipperReference, decimal Total);
