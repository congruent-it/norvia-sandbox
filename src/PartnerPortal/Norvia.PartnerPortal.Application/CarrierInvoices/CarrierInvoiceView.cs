using Norvia.PartnerPortal.Domain.CarrierInvoices;

namespace Norvia.PartnerPortal.Application.CarrierInvoices;

/// <summary>
/// A Carrier invoice of the Carrier Persona, with the result Norvia gave each line on receipt and, once Norvia has told
/// the outcome, what became of each line. An invoice the Persona sent from the portal is kept whole. One it sent another way,
/// such as the Simulator through the Partner API, is known only by the outcome Norvia told (<see cref="SentFromPortal"/> is false):
/// it has no date, no claimed amounts and no result on receipt.
/// </summary>
/// <param name="InvoiceDate">The date on the invoice; null for an invoice not sent from the portal.</param>
/// <param name="SubmittedAt">When the portal sent the invoice; for one not sent from the portal, when Norvia told its outcome.</param>
/// <param name="Total">What the invoice claims; null for an invoice not sent from the portal.</param>
/// <param name="OutcomeReceivedAt">When Norvia told the outcome; null until it does.</param>
/// <param name="SentFromPortal">False for an invoice the portal never saw, which is listed from its outcome alone.</param>
public sealed record CarrierInvoiceView(
    string InvoiceNumber,
    DateOnly? InvoiceDate,
    DateTimeOffset SubmittedAt,
    decimal? Total,
    CarrierInvoiceStatus Status,
    decimal? ApprovedAmount,
    string CorrelationId,
    IReadOnlyList<CarrierInvoiceLineView> Lines,
    DateTimeOffset? OutcomeReceivedAt,
    bool SentFromPortal = true)
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

    /// <summary>An invoice the portal never saw, as far as Norvia's outcome tells it: its number, its lines and what became of each.</summary>
    public static CarrierInvoiceView FromOutcome(CarrierInvoiceOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var lines = outcome.Lines.Select(line => new CarrierInvoiceLineView(line.BookingRef, Amount: null, Description: null, Result: null, line)).ToList();
        return new(
            outcome.InvoiceNumber, InvoiceDate: null, outcome.ReceivedAt, Total: null, CarrierInvoiceStatus.Approved, outcome.ApprovedAmount, outcome.CorrelationId, lines, outcome.ReceivedAt,
            SentFromPortal: false);
    }
}

/// <param name="Amount">What the Carrier claimed for the line; null for an invoice not sent from the portal.</param>
/// <param name="Result">What Norvia made of the line on receipt; null for an invoice not sent from the portal.</param>
/// <param name="Outcome">What became of the line once Norvia told the outcome; null until it does.</param>
public sealed record CarrierInvoiceLineView(string BookingRef, decimal? Amount, string? Description, CarrierInvoiceLineResult? Result, CarrierInvoiceOutcomeLine? Outcome);
