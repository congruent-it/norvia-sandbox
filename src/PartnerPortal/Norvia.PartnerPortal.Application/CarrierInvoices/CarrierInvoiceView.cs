using Norvia.PartnerPortal.Domain.CarrierInvoices;

namespace Norvia.PartnerPortal.Application.CarrierInvoices;

/// <summary>
/// A Carrier invoice the Carrier Persona sent, with the result Norvia gave each line on receipt and, once Norvia has told
/// the outcome, what became of each line.
/// </summary>
/// <param name="OutcomeReceivedAt">When Norvia told the outcome; null until it does.</param>
public sealed record CarrierInvoiceView(
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateTimeOffset SubmittedAt,
    decimal Total,
    CarrierInvoiceStatus Status,
    decimal? ApprovedAmount,
    string CorrelationId,
    IReadOnlyList<CarrierInvoiceLineView> Lines,
    DateTimeOffset? OutcomeReceivedAt)
{
    public bool OutcomeTold => OutcomeReceivedAt is not null;

    public static CarrierInvoiceView From(CarrierInvoice invoice, CarrierInvoiceOutcome? outcome)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var lines = invoice.Lines
            .Select(line => new CarrierInvoiceLineView(
                line.BookingRef, line.Amount, line.Description, line.Result, outcome?.Lines.FirstOrDefault(told => told.BookingRef == line.BookingRef)))
            .ToList();
        return outcome is null
            ? new(invoice.InvoiceNumber, invoice.InvoiceDate, invoice.SubmittedAt, invoice.Total, invoice.Status, invoice.ApprovedAmount, invoice.CorrelationId, lines, OutcomeReceivedAt: null)
            : new(invoice.InvoiceNumber, invoice.InvoiceDate, invoice.SubmittedAt, invoice.Total, CarrierInvoiceStatus.Approved, outcome.ApprovedAmount, invoice.CorrelationId, lines, outcome.ReceivedAt);
    }
}

/// <param name="Result">What Norvia made of the line on receipt.</param>
/// <param name="Outcome">What became of the line once Norvia told the outcome; null until it does.</param>
public sealed record CarrierInvoiceLineView(string BookingRef, decimal Amount, string? Description, CarrierInvoiceLineResult Result, CarrierInvoiceOutcomeLine? Outcome);
