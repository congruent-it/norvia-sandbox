using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>
/// A Carrier invoice a Carrier Persona sent Norvia and Norvia received (E2). The Carrier's invoice number is its key:
/// the same number is the same invoice. It is the Carrier's own record of what it claimed and how Norvia matched each
/// line; what Finance decided comes later, as a <see cref="CarrierInvoiceOutcome"/>.
/// </summary>
public sealed class CarrierInvoice : EntityBase<CarrierInvoiceId>, IAggregateRoot
{
    // For EF Core, which sets the lines from their column.
    private CarrierInvoice()
    {
        PersonaId = null!;
        InvoiceNumber = null!;
        CorrelationId = null!;
        Lines = null!;
    }

    private CarrierInvoice(
        CarrierInvoiceId id,
        PersonaId personaId,
        string invoiceNumber,
        DateOnly invoiceDate,
        string correlationId,
        DateTimeOffset submittedAt,
        CarrierInvoiceStatus status,
        decimal? approvedAmount,
        IReadOnlyList<CarrierInvoiceLine> lines)
    {
        Id = id;
        PersonaId = personaId;
        InvoiceNumber = invoiceNumber;
        InvoiceDate = invoiceDate;
        CorrelationId = correlationId;
        SubmittedAt = submittedAt;
        Status = status;
        ApprovedAmount = approvedAmount;
        Lines = lines;
    }

    public PersonaId PersonaId { get; private set; }

    /// <summary>The Carrier's own number of the invoice, the key of its list.</summary>
    public string InvoiceNumber { get; private set; }

    public DateOnly InvoiceDate { get; private set; }

    /// <summary>The correlation ID API Management gave the invoice, by which Norvia finds every trace of it.</summary>
    public string CorrelationId { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    /// <summary>The status Norvia gave the invoice when it received it.</summary>
    public CarrierInvoiceStatus Status { get; private set; }

    /// <summary>The sum of the Matched amounts, once the invoice was Approved on receipt.</summary>
    public decimal? ApprovedAmount { get; private set; }

    public IReadOnlyList<CarrierInvoiceLine> Lines { get; private set; }

    public decimal Total => Lines.Sum(line => line.Amount);

    public static CarrierInvoice Receive(
        PersonaId personaId,
        string invoiceNumber,
        DateOnly invoiceDate,
        string correlationId,
        DateTimeOffset submittedAt,
        CarrierInvoiceStatus status,
        decimal? approvedAmount,
        IReadOnlyList<CarrierInvoiceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("A Carrier invoice has at least one line.", nameof(lines));
        }

        return new CarrierInvoice(CarrierInvoiceId.New(), personaId, invoiceNumber, invoiceDate, correlationId, submittedAt, status, approvedAmount, [.. lines]);
    }
}
