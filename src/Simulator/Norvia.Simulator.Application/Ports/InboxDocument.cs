namespace Norvia.Simulator.Application.Ports;

/// <summary>An invoice (<c>INVOICE</c>) or Credit note (<c>CREDIT_NOTE</c>) in a Shipper's invoices inbox, with the trail it arrived on.</summary>
public sealed record InboxDocument(string DocumentNumber, string DocumentType, string? CorrelationId);
