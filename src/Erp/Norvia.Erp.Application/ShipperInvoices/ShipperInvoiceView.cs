namespace Norvia.Erp.Application.ShipperInvoices;

/// <summary>A Shipper invoice as the ERP shows it and publishes it, in the Canonical Model's terms.</summary>
public sealed record ShipperInvoiceView(
    string InvoiceNumber,
    DateOnly InvoiceDate,
    ShipperOnInvoiceView Shipper,
    IReadOnlyList<ShipperInvoiceLineView> Lines,
    string Currency,
    decimal TotalWithoutVat,
    decimal Vat,
    decimal TotalWithVat,
    DateTimeOffset IssuedAt,
    string CorrelationId,
    string Status);

public sealed record ShipperOnInvoiceView(string ShipperId, string Name);

/// <param name="DeliveryDate">The day of delivery where it happened.</param>
public sealed record ShipperInvoiceLineView(
    string TransportOrderNumber,
    string ShipperReference,
    string ShipmentNumber,
    DateOnly DeliveryDate,
    IReadOnlyList<InvoiceChargeLineView> ChargeLines,
    decimal Total);

public sealed record InvoiceChargeLineView(string Type, decimal Amount);
