using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// Norvia's answer to a Carrier invoice, as the log shows it. <see cref="Outcome"/> is <see cref="LogOutcome.CarrierInvoiced"/> when Norvia took the invoice in
/// (then <see cref="Status"/> is <c>RECEIVED</c> or <c>APPROVED</c> and every line has its result), otherwise a refusal or not received. <see cref="Detail"/> holds a
/// refusal's code, never the text Norvia words it with.
/// </summary>
public sealed record CarrierInvoiceAnswer(LogOutcome Outcome, string? CorrelationId, string? Status, IReadOnlyList<CarrierInvoiceLineAnswer> Lines, string? Detail)
{
    public bool Approved => Status == "APPROVED";
}

/// <param name="Result">The wire result of the line: <c>MATCHED</c>, <c>PRICE_MISMATCH</c>, <c>UNKNOWN_BOOKING</c> or <c>DUPLICATE</c>.</param>
public sealed record CarrierInvoiceLineAnswer(string BookingReference, decimal Amount, string Result)
{
    public bool Matched => Result == "MATCHED";
}
