namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// A Carrier Persona's side of the Partner portal, read with the Carrier's own endpoint key: the price Norvia offered for each Carrier booking in its inbox, which is
/// what it agreed to and invoices, and the outcomes of its Carrier invoices as they arrive on its invoices page. Reading changes nothing.
/// </summary>
public interface ICarrierInbox
{
    /// <summary>What Norvia offered, in EUR without VAT, by Carrier booking number; null when the inbox couldn't be read (the portal unreachable, or no credential for the Carrier).</summary>
    Task<IReadOnlyDictionary<string, decimal>?> AgreedPricesAsync(string carrierId, CancellationToken cancellationToken);

    /// <summary>The outcomes Norvia has told the Carrier, or null when they couldn't be read.</summary>
    Task<IReadOnlyList<CarrierInvoiceOutcome>?> OutcomesAsync(string carrierId, CancellationToken cancellationToken);
}

/// <param name="InvoiceNumber">The Carrier's own invoice number.</param>
/// <param name="CorrelationId">The trail the outcome arrived on.</param>
public sealed record CarrierInvoiceOutcome(string InvoiceNumber, string CorrelationId, decimal ApprovedAmount, IReadOnlyList<CarrierOutcomeLine> Lines);

/// <param name="Result">The wire result: <c>MATCHED</c>, <c>ACCEPTED</c>, <c>ACCEPTED_ADJUSTED</c> or <c>REJECTED</c>.</param>
/// <param name="Remark">Finance's remark on an adjusted amount, or its reason for a rejection.</param>
public sealed record CarrierOutcomeLine(string BookingReference, string Result, decimal AcceptedAmount, string? Remark);
