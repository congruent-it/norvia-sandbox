namespace Norvia.Erp.Api.CarrierInvoices;

/// <summary>
/// The Carrier invoice, version 1, as the Canonical Model has it (ADR 0003). The ERP billing API accepts nothing else: a member outside the
/// model, or a missing one, is refused. The <c>carrierId</c> is the one API Management took from the token (ADR 0005); the Carrier invoice
/// Logic App puts it here, and what the Carrier wrote never does.
/// </summary>
public sealed record CarrierInvoiceRequest(
    string CarrierId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string CorrelationId,
    IReadOnlyList<CarrierInvoiceLineRequest> Lines);

public sealed record CarrierInvoiceLineRequest(string CarrierBookingReference, decimal Amount, string? Description = null);
