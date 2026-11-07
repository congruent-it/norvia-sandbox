namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

/// <param name="InvoiceNumber">The Carrier's invoice number of the earlier invoice.</param>
/// <param name="Status">How the earlier line was decided: <c>Matched</c> or <c>Accepted</c>.</param>
/// <param name="Amount">What the earlier line contributes to its invoice's approved amount.</param>
public sealed record AgentEarlierLineView(
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string CarrierBookingReference,
    string Status,
    decimal Amount);
