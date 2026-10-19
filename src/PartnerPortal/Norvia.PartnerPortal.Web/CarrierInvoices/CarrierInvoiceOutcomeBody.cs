namespace Norvia.PartnerPortal.Web.CarrierInvoices;

/// <summary>
/// The outcome of a Carrier invoice in the Carrier invoice outcome format (E2), as the Carrier invoice outcome Logic App
/// sends it: the Carrier's invoice number, the approved amount and, per line, the result and what Finance accepted.
/// </summary>
public sealed record CarrierInvoiceOutcomeBody(string? InvoiceNo, decimal? ApprovedAmount, IReadOnlyList<CarrierInvoiceOutcomeLineBody?>? Lines);

public sealed record CarrierInvoiceOutcomeLineBody(string? BookingRef, string? Result, decimal? AcceptedAmount, string? Remark);
