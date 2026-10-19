using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>
/// What Norvia told a Carrier Persona about an Approved Carrier invoice (E2). The Carrier's invoice number is its key, so
/// an outcome is stored once: the same number told again is the outcome the Carrier already has. It stands on its own, not
/// inside the <see cref="CarrierInvoice"/>, because Norvia can tell the outcome of an invoice approved on receipt before
/// the Persona has stored the answer to it.
/// </summary>
public sealed class CarrierInvoiceOutcome : EntityBase<CarrierInvoiceOutcomeId>, IAggregateRoot
{
    // For EF Core, which sets the lines from their column.
    private CarrierInvoiceOutcome()
    {
        PersonaId = null!;
        InvoiceNumber = null!;
        CorrelationId = null!;
        Lines = null!;
    }

    private CarrierInvoiceOutcome(
        CarrierInvoiceOutcomeId id,
        PersonaId personaId,
        string invoiceNumber,
        string correlationId,
        DateTimeOffset receivedAt,
        decimal approvedAmount,
        IReadOnlyList<CarrierInvoiceOutcomeLine> lines)
    {
        Id = id;
        PersonaId = personaId;
        InvoiceNumber = invoiceNumber;
        CorrelationId = correlationId;
        ReceivedAt = receivedAt;
        ApprovedAmount = approvedAmount;
        Lines = lines;
    }

    public PersonaId PersonaId { get; private set; }

    public string InvoiceNumber { get; private set; }

    /// <summary>The correlation ID the outcome arrived with.</summary>
    public string CorrelationId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>The sum of the Matched and Accepted amounts.</summary>
    public decimal ApprovedAmount { get; private set; }

    public IReadOnlyList<CarrierInvoiceOutcomeLine> Lines { get; private set; }

    public static CarrierInvoiceOutcome Receive(
        PersonaId personaId,
        string invoiceNumber,
        string correlationId,
        DateTimeOffset receivedAt,
        decimal approvedAmount,
        IReadOnlyList<CarrierInvoiceOutcomeLine> lines)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(lines);
        return new CarrierInvoiceOutcome(CarrierInvoiceOutcomeId.New(), personaId, invoiceNumber, correlationId, receivedAt, approvedAmount, [.. lines]);
    }

    /// <summary>Whether Norvia rejected the line with this reference.</summary>
    public bool Rejects(string bookingRef) =>
        Lines.Any(line => line.BookingRef == bookingRef && line.Result == CarrierInvoiceOutcomeResult.Rejected);
}
