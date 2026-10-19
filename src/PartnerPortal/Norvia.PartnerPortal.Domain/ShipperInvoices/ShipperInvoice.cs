using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.ShipperInvoices;

/// <summary>
/// An invoice or Credit note Norvia sent a Shipper Persona, in the Shipper's invoices inbox. The document number is its
/// key: the same document received again is the one the inbox already has.
/// </summary>
public sealed class ShipperInvoice : EntityBase<ShipperInvoiceId>, IAggregateRoot
{
    // For EF Core, which sets the content from its column.
    private ShipperInvoice()
    {
        PersonaId = null!;
        DocumentNumber = null!;
        CorrelationId = null!;
        Content = null!;
    }

    private ShipperInvoice(ShipperInvoiceId id, PersonaId personaId, string correlationId, DateTimeOffset receivedAt, ShipperInvoiceContent content)
    {
        Id = id;
        PersonaId = personaId;
        DocumentNumber = content.DocumentNumber;
        CorrelationId = correlationId;
        ReceivedAt = receivedAt;
        Content = content;
    }

    public PersonaId PersonaId { get; private set; }

    /// <summary>Norvia's number of the document, the key of the inbox.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>The correlation ID the document arrived with.</summary>
    public string CorrelationId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public ShipperInvoiceContent Content { get; private set; }

    public static ShipperInvoice Receive(PersonaId personaId, string correlationId, DateTimeOffset receivedAt, ShipperInvoiceContent content)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.DocumentNumber);
        return new ShipperInvoice(ShipperInvoiceId.New(), personaId, correlationId, receivedAt, content);
    }
}
