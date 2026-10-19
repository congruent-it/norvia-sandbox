namespace Norvia.Erp.Application.ShipperInvoices;

/// <summary>A Shipper invoice as the ERP shows it and publishes it, in the Canonical Model's terms.</summary>
/// <param name="SentAt">When the invoice was reported Sent; left out of the JSON while there is none.</param>
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
    string Status,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? SentAt = null);

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
